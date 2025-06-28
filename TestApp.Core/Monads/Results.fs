namespace TestApp.Core.Monads

open System
open System.Threading.Tasks

type public Result<'TSuccess, 'TFailure> =
    | Success of 'TSuccess
    | Failure of 'TFailure
    
    [<CompiledNameAttribute("IsSuccess")>]
    member public this.isSuccess : bool =
        match this with
        | Success _ -> true
        | Failure _ -> false

    [<CompiledNameAttribute("IsFailure")>]
    member public this.isFailure : bool = this.isSuccess |> not
    
    [<CompiledNameAttribute("Map")>]
    member public this.map<'TNewSuccess> (f: Func<'TSuccess, 'TNewSuccess>) : Result<'TNewSuccess, 'TFailure> =
        match this with
        | Success v -> v |> f.Invoke |> Success
        | Failure e -> e |> Failure

    [<CompiledNameAttribute("MapError")>]
    member public this.mapError<'TNewFailure>(f: Func<'TFailure, 'TNewFailure>) : Result<'TSuccess, 'TNewFailure> =
        match this with
        | Success v -> v |> Success
        | Failure e -> e |> f.Invoke |> Failure

    [<CompiledNameAttribute("Bind")>]
    member public this.bind<'TNewSuccess>(f: Func<'TSuccess, Result<'TNewSuccess, 'TFailure>>) : Result<'TNewSuccess, 'TFailure> =
        match this with
        | Success v -> v |> f.Invoke
        | Failure e -> e |> Failure

    [<CompiledName("Match")>]
    member public this.matchResult<'TResult>(onOk: Func<'TSuccess, 'TResult>, onError: Func<'TFailure, 'TResult>) : 'TResult =
        match this with
        | Success v -> v |> onOk.Invoke
        | Failure e -> e |> onError.Invoke

    [<CompiledName("Match")>]
    member public this.matchOnError<'TResult>(onOk: Func<'TSuccess, 'TResult>, onError: 'TResult) : 'TResult =
        match this with
        | Success v -> v |> onOk.Invoke
        | Failure _ -> onError

    [<CompiledName("Match")>]
    member public this.matchOnOk<'TResult>(onOk: 'TResult, onError: Func<'TFailure, 'TResult>) : 'TResult =
        match this with
        | Success _ -> onOk
        | Failure e -> e |> onError.Invoke

    [<CompiledName("Match")>]
    member public this.matchConst<'TResult>(onOk: 'TResult, onError: 'TResult) : 'TResult =
        match this with
        | Success _ -> onOk
        | Failure _ -> onError

    [<CompiledName("Match")>]
    member public this.matchUnit(onOk: Action<'TSuccess>, onError: Action<'TFailure>) : unit =
        match this with
        | Success v -> v |> onOk.Invoke
        | Failure e -> e |> onError.Invoke

    [<CompiledName("Match")>]
    member public this.matchTask<'TResult>(onOk: Func<'TSuccess, Task<'TResult>>, onError: Func<'TFailure, Task<'TResult>>) : Task<'TResult> =
        match this with
        | Success v ->
            task {
                let! result = v |> onOk.Invoke
                return result
            }
        | Failure e ->
            task {
                let! result = e |> onError.Invoke
                return result
            }

    [<CompiledNameAttribute("Tee")>]
    member public this.tee(fValue: Action<'TSuccess>, fErr: Action<'TFailure>) : Result<'TSuccess, 'TFailure> =
        match this with
        | Success v -> v |> fValue.Invoke; v |> Success
        | Failure e -> e |> fErr.Invoke; e |> Failure