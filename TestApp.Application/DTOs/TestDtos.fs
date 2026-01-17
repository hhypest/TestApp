namespace TestApp.Application.DTOs

open System

/// <summary>
/// Data Transfer Object for creating a new test
/// </summary>
type public CreateTestDto =
    { TestTitle: string
      TestTime: TimeOnly }

/// <summary>
/// Data Transfer Object for updating a test
/// </summary>
type public UpdateTestDto =
    { TestId: Guid
      TestTitle: string
      TestTime: TimeOnly }

/// <summary>
/// Data Transfer Object for test response
/// </summary>
type public TestDto =
    { TestId: Guid
      TestTitle: string
      TestTime: TimeOnly
      Questions: AskDto list }

and public AskDto =
    { AskId: Guid
      AskTitle: string
      IsSingle: bool
      Answers: AnswerDto list }

and public AnswerDto =
    { AnswerId: Guid
      AnswerTitle: string
      IsCorrect: bool }

/// <summary>
/// Pagination request DTO
/// </summary>
type public PaginationRequest =
    { PageNumber: int
      PageSize: int }

/// <summary>
/// Paginated response DTO
/// </summary>
type public PaginatedResponse<'T> =
    { Items: 'T list
      TotalCount: int
      PageNumber: int
      PageSize: int
      TotalPages: int }
