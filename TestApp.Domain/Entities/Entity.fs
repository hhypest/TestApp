namespace TestApp.Domain.Entities

open System
open System.Collections.Generic
open TestApp.Core.Events

[<AbstractClass>]
type public Entity() =
    class
        let domainEvents: ResizeArray<DomainEvent> = ResizeArray<DomainEvent>()
        member public _.DomainEvents: IReadOnlyCollection<DomainEvent> = domainEvents.AsReadOnly()

        member public _.AddDomainEvent (domainEvent: DomainEvent) : unit =
            domainEvent |> domainEvents.Add

        member public _.RemoveDomainEvent (domainEvent: DomainEvent) : bool =
            domainEvent |> domainEvents.Remove

        member public _.ClearDomainEvents () : unit =
            domainEvents.Clear()
    end

type public AnswerEntity() =
    class
        inherit Entity()
        member val public AnswerId: Guid = Guid.Empty with get, set
        member val public AskId: Guid = Guid.Empty with get, set
        member val public AnswerTitle: string = String.Empty with get, set
        member val public IsCorrect: bool = false with get, set
        member val public Ask: AskEntity = Unchecked.defaultof<_> with get, set
    end

and public AskEntity() =
    class
        inherit Entity()
        member val public AskId: Guid = Guid.Empty with get, set
        member val public TestId: Guid = Guid.Empty with get, set
        member val public AskTitle: string = String.Empty with get, set
        member val public IsSingle: bool = false with get, set
        member val public AnswersList: seq<AnswerEntity> = Seq.empty with get, set
        member val public Test: TestEntity = Unchecked.defaultof<_> with get, set
    end

and public TestEntity() =
    class
        inherit Entity()
        member val public TestId: Guid = Guid.Empty with get, set
        member val public TestTitle: string = String.Empty with get, set
        member val public TestTime: TimeOnly = TimeOnly.MinValue with get, set
        member val public AsksList: seq<AskEntity> = Seq.empty with get, set
    end