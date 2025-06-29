namespace TestApp.Domain.Abstractions

open System
open System.Threading
open System.Threading.Tasks
open TestApp.Core.Types
open TestApp.Core.Monads
open TestApp.Domain.Entities

type public IAnswerRepository =
    interface
        abstract member GetById : answerId: Guid -> token: CancellationToken -> Task<Result<AnswerData, Error>>
        abstract member GetAllAnswers : askId: Guid -> token: CancellationToken -> Task<Result<seq<AnswerData>, Error>>
        abstract member CreateAnswer : answer: AnswerEntity -> token: CancellationToken -> Task<Result<int, Error>>
        abstract member UpdateAnswer : answer: AnswerEntity -> token: CancellationToken -> Task<Result<int, Error>>
        abstract member DeleteAnswer : answerId: Guid -> token: CancellationToken -> Task<Result<int, Error>>
        abstract member DeleteAllAnswers : askId: Guid -> token: CancellationToken -> Task<Result<int, Error>>
    end

type public IAskRepository =
    interface
        abstract member GetById : askId: Guid -> token: CancellationToken -> Task<Result<AskEntity, Error>>
        abstract member GetAllAsks : testId: Guid -> token: CancellationToken -> Task<Result<seq<AskEntity>, Error>>
        abstract member GetAsksPage : testId: Guid -> pageNumber: int -> pageSize: int -> token: CancellationToken -> Task<Result<seq<AskEntity>, Error>>
        abstract member CreateAsk : ask: AskEntity -> token: CancellationToken -> Task<Result<int, Error>>
        abstract member UpdateAsk : ask: AskEntity -> token: CancellationToken -> Task<Result<int, Error>>
        abstract member DeleteAsk : askId: Guid -> token: CancellationToken -> Task<Result<int, Error>>
    end

type public ITestRepository =
    interface
        abstract member GetById : testId: Guid -> token: CancellationToken -> Task<Result<TestEntity, Error>>
        abstract member GetAllTests : token: CancellationToken -> Task<Result<seq<TestEntity>, Error>>
        abstract member GetTestsPage : pageNumber: int -> pageSize: int -> token: CancellationToken -> Task<Result<seq<TestEntity>, Error>>
        abstract member CreateTest : test: TestEntity -> token: CancellationToken -> Task<Result<int, Error>>
        abstract member UpdateTest : test: TestEntity -> token: CancellationToken -> Task<Result<int, Error>>
        abstract member DeleteTest : testId: Guid -> token: CancellationToken -> Task<Result<int, Error>>
    end