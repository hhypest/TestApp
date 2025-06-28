namespace TestApp.Core.Types

open System
open TestApp.Core.Monads
open TestApp.Core.Messages
open TestApp.Core.Types

type public PageParams = 
    {
        Page: int
        PageSize: int
    }

type public GetAllTestsQuery =
    {
        Params: PageParams
    }
    interface IQuery<Result<seq<TestsData>, Error>>

type public GetTestQuery =
    {
        TestId: Guid
    }
    interface IQuery<Result<TestData, Error>>

type public GetTestPageQuery =
    {
        TestId: Guid
        Params: PageParams
    }
    interface IQuery<Result<TestData, Error>>

type public GetAsksQuery = 
    {
        TestId: Guid
    }
    interface IQuery<Result<seq<AskData>, Error>>

type public GetAskQuery = 
    {
        AskId: Guid
    }
    interface IQuery<Result<AskData, Error>>

type public GetAnswersQuery = 
    {
        AskId: Guid
    }
    interface IQuery<Result<seq<AnswerData>, Error>>

type public GetAnswerQuery = 
    {
        AnswerId: Guid
    }
    interface IQuery<Result<AnswerData, Error>>