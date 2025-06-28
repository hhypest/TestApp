namespace TestApp.Core.Events

open System
open TestApp.Core.Types

type public DomainEvent =
    | TestCreated of EventId: Guid * CreatedAt: DateTime * Test: TestData
    | AskCreated of EventId: Guid * CreatedAt: DateTime * Ask: AskData
    | AnswerCreated of EventId: Guid * CreatedAt: DateTime * Answer: AnswerData