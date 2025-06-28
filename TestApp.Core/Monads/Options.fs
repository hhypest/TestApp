namespace TestApp.Core.Monads

open System
open System.Threading.Tasks

type public Option<'TValue> =
    | Some of 'TValue
    | None

    [<CompiledNameAttribute("IsSome")>]
    member public this.isSome : bool =
        match this with
            | Some _ -> true
            | None -> false

    [<CompiledNameAttribute("IsNone")>]
    member public this.isNone : bool = this.isSome |> not
    
    [<CompiledNameAttribute("Map")>]
    member public this.map<'U>(f: Func<'TValue, 'U>) : Option<'U> =
        match this with
            | Some v -> v |> f.Invoke |> Some
            | None -> None

    [<CompiledNameAttribute("Bind")>]
    member public this.bind<'U>(f: Func<'TValue, Option<'U>>) : Option<'U> =
        match this with
            | Some v -> v |> f.Invoke
            | None -> None

    [<CompiledNameAttribute("Match")>]
    member public this.matchOption<'TResult>(onSome: Func<'TValue, 'TResult>, onNone: Func<'TResult>) : 'TResult =
        match this with
            | Some v -> v |> onSome.Invoke
            | None -> onNone.Invoke()

    [<CompiledNameAttribute("Match")>]
    member public this.matchOptionOnSome<'TResult>(onSome: 'TResult, onNone: Func<'TResult>) : 'TResult =
        match this with
            | Some _ -> onSome
            | None -> onNone.Invoke()

    [<CompiledNameAttribute("Match")>]
    member public this.matchOptionOnNone<'TResult>(onSome: Func<'TValue, 'TResult>, onNone: 'TResult) : 'TResult =
        match this with
            | Some v -> v |> onSome.Invoke
            | None -> onNone

    [<CompiledNameAttribute("Match")>]
    member public this.matchOptionConst<'TResult>(onSome: 'TResult, onNone: 'TResult) : 'TResult =
        match this with
            | Some _ -> onSome
            | None -> onNone

    [<CompiledNameAttribute("Match")>]
    member public this.matchOptionTask<'TResult>(onSome: Func<'TValue, Task<'TResult>>, onNone: Func<Task<'TResult>>) : Task<'TResult> =
        match this with
            | Some v ->
                task {
                    let! result = v |> onSome.Invoke
                    return result
                }
            | None ->
                task {
                    let! result = onNone.Invoke()
                    return result
                }

    [<CompiledNameAttribute("Tee")>]
    member public this.tee(fSome: Action<'TValue>, fNone: Action) : Option<'TValue> =
        match this with
            | Some v -> v |> fSome.Invoke; Some v
            | None -> fNone.Invoke(); None