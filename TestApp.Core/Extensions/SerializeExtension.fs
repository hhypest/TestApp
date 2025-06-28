namespace TestApp.Core.Extensions

open System
open System.Text.Json
open System.Runtime.CompilerServices
open Microsoft.FSharp.Core
open TestApp.Core.Events
open TestApp.Core.Monads

module private JOpts =

    let options =
        lazy(
            let opts = JsonSerializerOptions()
            opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
            opts.WriteIndented <- true
            opts.Converters.Add(DomainEventConverter())
            opts
        )

[<Extension>]
type public DomainEventExtension() =
        [<Extension>]
        [<CompiledNameAttribute("ToJson")>]
        static member public toJson(domainEvent: DomainEvent) : Result<string, exn> =
            try
                let json = JsonSerializer.Serialize<DomainEvent>(domainEvent, JOpts.options.Value)
                let isSuccess = json |> String.IsNullOrWhiteSpace |> not
                match isSuccess with
                    | true -> json |> Success
                    | false -> "An error occurred during the process of serializing an object." |> Exception |> Failure
            with ex -> ex |> Failure

        [<Extension>]
        [<CompiledNameAttribute("FromJson")>]
        static member public fromJson(json: string) : Result<DomainEvent, exn> =
            try
                let result = JsonSerializer.Deserialize<DomainEvent>(json, JOpts.options.Value) |> Success
                result
            with ex -> ex |> Failure