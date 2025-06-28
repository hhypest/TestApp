namespace TestApp.Core.Types

open System

[<AttributeUsage(AttributeTargets.Property)>]
type public IgnoreMapAttribute() =
    inherit Attribute()

[<AttributeUsage(AttributeTargets.Property)>]
type public NamedAttribute(targetName: string) =
    inherit Attribute()
    member _.TargetName = targetName

[<AttributeUsage(AttributeTargets.Class, AllowMultiple = true)>]
type public MapperAttribute(source: Type, target: Type) =
    inherit Attribute()
    member public _.Source = source
    member public _.Target = target