namespace TestApp.Domain.Queries

open System
open TestApp.Core.Messages
open TestApp.Domain.Entities

/// <summary>
/// Query to get a test by ID
/// </summary>
type public GetTestByIdQuery =
    { TestId: Guid }
    interface IQuery<TestEntity option>

/// <summary>
/// Query to get all tests
/// </summary>
type public GetAllTestsQuery() =
    interface IQuery<seq<TestEntity>>

/// <summary>
/// Query to get a test with all questions and answers
/// </summary>
type public GetTestWithDetailsQuery =
    { TestId: Guid }
    interface IQuery<TestEntity option>

/// <summary>
/// Query to get all questions for a specific test
/// </summary>
type public GetQuestionsByTestIdQuery =
    { TestId: Guid }
    interface IQuery<seq<AskEntity>>

/// <summary>
/// Query to get all answers for a specific question
/// </summary>
type public GetAnswersByQuestionIdQuery =
    { AskId: Guid }
    interface IQuery<seq<AnswerEntity>>

/// <summary>
/// Query to search tests by title
/// </summary>
type public SearchTestsByTitleQuery =
    { SearchTerm: string }
    interface IQuery<seq<TestEntity>>
