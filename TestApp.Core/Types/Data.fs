namespace TestApp.Core.Types

open System

type public AnswerData() =
    class
        member val AnswerId: Guid = Guid.Empty with get, set
        member val AskId: Guid = Guid.Empty with get, set
        member val AnswerTitle: string = String.Empty with get, set
        member val IsCorrect: bool = false with get, set
    end

type public AskData() =
    class
        member val AskId: Guid = Guid.Empty with get, set
        member val TestId: Guid = Guid.Empty with get, set
        member val AskTitle: string = String.Empty with get, set
        member val IsSingle: bool = false with get, set
        member val AnswersList: seq<AnswerData> = Seq.empty with get, set
    end

type public TestData() =
    class
        member val TestId: Guid = Guid.Empty with get, set
        member val TestTitle: string = String.Empty with get, set
        member val TestTime: TimeOnly = TimeOnly.MinValue with get, set
        member val AsksList: seq<AskData> = Seq.empty with get, set
    end

type public TestsData() =
    class
        member val TestId: Guid = Guid.Empty with get, set
        member val TestTitle: string = String.Empty with get, set
        member val TestTime: TimeOnly = TimeOnly.MinValue with get, set
        member val AskCount: int = 0 with get, set
    end