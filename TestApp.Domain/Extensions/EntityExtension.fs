namespace TestApp.Domain.Extensions

open System
open System.Runtime.CompilerServices
open TestApp.Core.Events
open TestApp.Core.Types
open TestApp.Core.Extensions
open TestApp.Domain.Entities

[<Extension>]
[<Mapper(typeof<AnswerEntity>, typeof<AnswerData>)>]
[<Mapper(typeof<AskEntity>, typeof<AskData>)>]
[<Mapper(typeof<TestEntity>, typeof<TestData>)>]
type public EntityExtension () =
    [<Extension>]
    static member public AnswerCreated(answer: AnswerEntity) : unit =
        let eventId = Guid.CreateVersion7()
        let eventDate = DateTime.UtcNow
        let eventPayload = answer |> DataMapper.map
        let createdEvent = (eventId, eventDate, eventPayload) |> DomainEvent.AnswerCreated
        createdEvent |> answer.AddDomainEvent

    [<Extension>]
    static member public AskCreated(ask: AskEntity) : unit =
        let eventId = Guid.CreateVersion7()
        let eventDate = DateTime.UtcNow
        let eventPayload = ask |> DataMapper.map
        let createdEvent = (eventId, eventDate, eventPayload) |> DomainEvent.AskCreated
        createdEvent |> ask.AddDomainEvent

    [<Extension>]
    static member public TestCreated(test: TestEntity) : unit =
        let eventId = Guid.CreateVersion7()
        let eventDate = DateTime.UtcNow
        let eventPayload = test |> DataMapper.map
        let createdEvent = (eventId, eventDate, eventPayload) |> DomainEvent.TestCreated
        createdEvent |> test.AddDomainEvent