namespace TestApp.Core.Types

open System
open TestApp.Core.Messages
open TestApp.Core.Types

type public PostAnswerCommand =
    {
        Query: AnswerData
    }
    interface ICommand

type public PutAnswerCommand =
    {
        Query: AnswerData
    }
    interface ICommand

type public DeleteAnswerCommand =
    {
        AnswerId: Guid
    }
    interface ICommand

type public DeleteAllAnswersCommand =
    {
        AskId: Guid
    }
    interface ICommand

type public PostAskCommand =
    {
        Query: AskData
    }
    interface ICommand

type public PutAskCommand =
    {
        Query: AskData
    }
    interface ICommand

type public DeleteAskCommand =
    {
        AskId: Guid
    }
    interface ICommand

type public DeleteAllAsksCommand =
    {
        TestId: Guid
    }
    interface ICommand

type public PostTestCommand =
    {
        Query: TestData
    }
    interface ICommand

type public PutTestCommand =
    {
        Query: TestData
    }
    interface ICommand

type public DeleteTestCommand =
    {
        TestId: Guid
    }
    interface ICommand