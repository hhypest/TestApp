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

    [<Extension>]
    static member public AnswerToDto(answerEntity: AnswerEntity) : AnswerData =
        answerEntity |> DataMapper.map

    [<Extension>]
    static member public AskToDto(askEntity: AskEntity) : AskData =
        let answersList = askEntity.AnswersList |> Seq.map(fun a -> a |> DataMapper.map<AnswerEntity, AnswerData>)
        let askDto = askEntity |> DataMapper.map<AskEntity, AskData>
        askDto.AnswersList <- answersList
        askDto

    [<Extension>]
    static member public TestToDto(testEntity: TestEntity) : TestData =
        let asksList = testEntity.AsksList |> Seq.map(fun a -> a |> DataMapper.map<AskEntity, AskData>)
        let testDto = testEntity |> DataMapper.map<TestEntity, TestData>
        testDto.AsksList <- asksList
        testDto

    [<Extension>]
    static member public TestToInfo(testEntity: TestEntity) : TestsData =
        let testInfo = TestsData()
        testInfo.TestId <- testEntity.TestId
        testInfo.TestTitle <- testEntity.TestTitle
        testInfo.TestTime <- testEntity.TestTime
        testInfo.AskCount <- testEntity.AsksList |> Seq.length
        testInfo

    [<Extension>]
    static member public AnswerToEntity(answerDto: AnswerData) : AnswerEntity =
        answerDto |> DataMapper.mapBack

    [<Extension>]
    static member public AskToEntity(askDto: AskData) : AskEntity =
        let answersList = askDto.AnswersList |> Seq.map(fun a -> a |> DataMapper.mapBack<AnswerEntity, AnswerData>)
        let askEntity = askDto |> DataMapper.mapBack<AskEntity, AskData>
        askEntity.AnswersList <- answersList
        askEntity

    [<Extension>]
    static member public TestToEntity(testDto: TestData) : TestEntity =
        let asksList = testDto.AsksList |> Seq.map(fun a -> a |> DataMapper.mapBack<AskEntity, AskData>)
        let testEntity = testDto |> DataMapper.mapBack<TestEntity, TestData>
        testEntity.AsksList <- asksList
        testEntity