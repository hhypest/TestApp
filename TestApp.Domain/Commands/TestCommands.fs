namespace TestApp.Domain.Commands

open System
open TestApp.Core.Messages
open TestApp.Domain.Entities

/// <summary>
/// Command to create a new test
/// </summary>
type public CreateTestCommand =
    { TestTitle: string
      TestTime: TimeOnly }
    interface ICommand

/// <summary>
/// Command to update an existing test
/// </summary>
type public UpdateTestCommand =
    { TestId: Guid
      TestTitle: string
      TestTime: TimeOnly }
    interface ICommand

/// <summary>
/// Command to delete a test
/// </summary>
type public DeleteTestCommand =
    { TestId: Guid }
    interface ICommand

/// <summary>
/// Command to add a question to a test
/// </summary>
type public AddQuestionToTestCommand =
    { TestId: Guid
      AskTitle: string
      IsSingle: bool }
    interface ICommand

/// <summary>
/// Command to add an answer to a question
/// </summary>
type public AddAnswerToQuestionCommand =
    { AskId: Guid
      AnswerTitle: string
      IsCorrect: bool }
    interface ICommand

/// <summary>
/// Response for test-related commands
/// </summary>
type public TestCommandResult =
    { Success: bool
      Message: string
      EntityId: Guid option }
