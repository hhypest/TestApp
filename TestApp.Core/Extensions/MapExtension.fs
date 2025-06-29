namespace TestApp.Core.Extensions

open System
open System.Collections.Frozen
open System.Collections.Generic
open System.Linq
open System.Linq.Expressions
open System.Reflection
open Microsoft.FSharp.Core
open TestApp.Core.Types

module public DataMapper =

    [<Sealed>]
    type private DataMapperPlaceholder =
        class
        end
    
    type private Rule<'TSource, 'TTarget> = {
        From: Func<'TSource, 'TTarget>
        To: Func<'TTarget, 'TSource>
    }

    let private getProfiledPairs(types: Type seq) : (Type * Type) seq =
        types
        |> Seq.collect(fun t ->
            t.GetCustomAttributes(typeof<MapperAttribute>, true)
            |> Seq.cast<MapperAttribute>
            |> Seq.map (fun attr -> attr.Source, attr.Target))

    let private isReadableWritable(p: PropertyInfo) =
        p.CanRead && p.CanWrite

    let private isIgnored(p: PropertyInfo) =
        not (Attribute.IsDefined(p, typeof<IgnoreMapAttribute>))

    let private getValidProperties(t: Type) : PropertyInfo seq =
        t.GetProperties(BindingFlags.Instance ||| BindingFlags.Public)
        |> Seq.filter(fun p -> isReadableWritable p && isIgnored p)

    let private getTargetPropertyName(p: PropertyInfo) =
        match p.GetCustomAttribute<NamedAttribute>() |> Option.ofObj with
        | None -> p.Name
        | Some attr -> attr.TargetName

    let private makeNamedMap(props: PropertyInfo seq) : IDictionary<string, PropertyInfo> =
        props |> Seq.map(fun p -> getTargetPropertyName p, p) |> dict

    let private isEnumerableType(t: Type) =
        t.GetInterfaces()
        |> Seq.exists (fun i -> i.IsGenericType && i.GetGenericTypeDefinition() = typedefof<IEnumerable<_>>)

    let private allTypes : Type seq =
        AppDomain.CurrentDomain.GetAssemblies()
        |> Seq.filter(fun asm ->
            let name = asm.GetName().Name
            not (name.StartsWith("System")) && not (name.StartsWith("Microsoft")))
        |> Seq.collect (fun asm -> asm.GetTypes())

    let private profiledTypes : Type seq =
        allTypes
        |> Seq.filter(fun t -> t.GetCustomAttributes(typeof<MapperAttribute>, true).Length > 0)

    let private knownTypes : Type seq =
        profiledTypes
        |> Seq.collect(fun t ->
            t.GetCustomAttributes(typeof<MapperAttribute>, true)
            |> Seq.cast<MapperAttribute>
            |> Seq.collect (fun attr -> [ attr.Source; attr.Target ]))
        |> Seq.distinct

    let private propertyCache : FrozenDictionary<Type, PropertyInfo seq> =
        knownTypes |> Seq.map(fun t -> t, getValidProperties t) |> dict |> FrozenDictionary.ToFrozenDictionary

    let private buildSimpleBinding(srcParam: ParameterExpression)(srcProp: PropertyInfo)(dstProp: PropertyInfo) =
        Expression.Bind(dstProp, Expression.Property(srcParam, srcProp))

    let private buildCollectionBinding(srcParam: ParameterExpression)(srcProp: PropertyInfo)(dstProp: PropertyInfo)(methodName: string) =
        let srcElementType = srcProp.PropertyType.GetGenericArguments().[0]
        let dstElementType = dstProp.PropertyType.GetGenericArguments().[0]

        let dataMapperType = typeof<DataMapperPlaceholder>.DeclaringType
        let mapMethod = 
            dataMapperType.GetMethod(methodName, BindingFlags.Public ||| BindingFlags.Static)
                .MakeGenericMethod([| srcElementType; dstElementType |])

        // Используем LINQ Select для преобразования коллекции
        let selectMethod = 
            typeof<Enumerable>.GetMethods()
            |> Seq.find (fun m -> m.Name = "Select" && m.GetParameters().Length = 2)
            |> fun m -> m.MakeGenericMethod([| srcElementType; dstElementType |])

        let srcValue = Expression.Property(srcParam, srcProp)
        let param = Expression.Parameter(srcElementType, "x")
        let mapCall = Expression.Call(mapMethod, param)
        let lambda = Expression.Lambda(mapCall, param)
        let mapped = Expression.Call(selectMethod, srcValue, lambda)
        Expression.Bind(dstProp, mapped)

    let private tryBuildBinding(srcParam: ParameterExpression)(srcProp: PropertyInfo)(dstProp: PropertyInfo)(methodName: string) : MemberBinding option =
        match srcProp.PropertyType, dstProp.PropertyType with
        | s, d when s = d -> Some (buildSimpleBinding srcParam srcProp dstProp)
        | s, d when isEnumerableType s && isEnumerableType d -> 
            Some (buildCollectionBinding srcParam srcProp dstProp methodName)
        | _ -> None

    let private tryBuildBindingFromProps(srcParam: ParameterExpression)(dstPropsMap: IDictionary<string, PropertyInfo>)(srcProp: PropertyInfo)(methodName: string) =
        let targetName = getTargetPropertyName srcProp
        match dstPropsMap.TryGetValue targetName with
        | true, dstProp -> tryBuildBinding srcParam srcProp dstProp methodName
        | _ -> None

    let private buildBindings(srcType: Type)(dstType: Type)(srcParam: ParameterExpression)(methodName: string) : ResizeArray<MemberBinding> =
        let srcProps = propertyCache.[srcType]
        let dstPropsMap = makeNamedMap propertyCache.[dstType]
        srcProps |> Seq.choose(fun p -> tryBuildBindingFromProps srcParam dstPropsMap p methodName) |> ResizeArray

    let private buildLambda<'a, 'b>(methodName: string) : Func<'a, 'b> =
        let srcParam = Expression.Parameter(typeof<'a>, "src")
        let bindings = buildBindings typeof<'a> typeof<'b> srcParam methodName
        Expression.Lambda<Func<'a, 'b>>(Expression.MemberInit(Expression.New(typeof<'b>), bindings), srcParam).Compile()

    let private createRule<'a, 'b>() : struct(Type * Type) * obj =
        let rule = {
            From = buildLambda<'a, 'b> "map"
            To = buildLambda<'b, 'a> "mapBack"
        }
        struct(typeof<'a>, typeof<'b>), box rule

    let private createRuleDynamic(src: Type)(dst: Type) : struct(Type * Type) * obj =
        let moduleType = typeof<DataMapperPlaceholder>.DeclaringType
        if isNull moduleType then
            invalidOp "DeclaringType for DataMapperPlaceholder is null"

        let flags = BindingFlags.NonPublic ||| BindingFlags.Static
        let methods = moduleType.GetMethods(flags)
        let mi = 
            methods 
            |> Array.tryFind (fun m -> 
                m.Name = "createRule" && 
                m.IsGenericMethodDefinition && 
                m.GetGenericArguments().Length = 2)
        
        match mi with
        | Some m ->
            let generic = m.MakeGenericMethod([| src; dst |])
            generic.Invoke(null, [||]) :?> struct(Type * Type) * obj
        | None ->
            invalidOp "createRule method not found"

    let private ruleCache : FrozenDictionary<struct(Type * Type), obj> =
        getProfiledPairs profiledTypes
        |> Seq.map (fun (src, dst) -> 
            try
                createRuleDynamic src dst
            with ex ->
                invalidOp $"Failed to create rule for {src.Name} -> {dst.Name}: {ex.Message}")
        |> dict
        |> FrozenDictionary.ToFrozenDictionary

    let public map<'a, 'b>(value: 'a) : 'b =
        let key = struct(typeof<'a>, typeof<'b>)
        match ruleCache.TryGetValue key with
        | true, rule -> (rule :?> Rule<'a, 'b>).From.Invoke(value)
        | _ -> invalidOp $"Mapping rule not found for {typeof<'a>} -> {typeof<'b>}"

    let public mapBack<'a, 'b>(value: 'b) : 'a =
        let key = struct(typeof<'a>, typeof<'b>)
        match ruleCache.TryGetValue key with
        | true, rule -> (rule :?> Rule<'a, 'b>).To.Invoke(value)
        | _ -> invalidOp $"Mapping rule not found for {typeof<'b>} -> {typeof<'a>}"