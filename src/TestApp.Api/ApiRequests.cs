using System.ComponentModel.DataAnnotations;
using TestApp.Domain.Assignments;
using TestApp.Domain.Identity;
using TestApp.Domain.Tests;

namespace TestApp.Api;

public sealed record CreateTestRequest(
    [property: Required, StringLength(TestLimits.TitleMaxLength)] string Title) : IApiRequest;

public sealed record RenameTestRequest(
    [property: Required, StringLength(TestLimits.TitleMaxLength)] string Title) : IApiRequest;

public sealed record TestSettingsRequest(decimal PassingPercentage, int? TimeLimitMinutes) : IApiRequest;

public sealed record QuestionWriteRequest(
    [property: Required, StringLength(TestLimits.QuestionTextMaxLength)] string Text,
    [property: EnumDataType(typeof(QuestionType))] QuestionType Type,
    decimal Points,
    int Order) : IApiRequest;

public sealed record QuestionUpdateRequest(
    [property: Required, StringLength(TestLimits.QuestionTextMaxLength)] string Text,
    [property: EnumDataType(typeof(QuestionType))] QuestionType Type,
    decimal Points) : IApiRequest;

public sealed record AnswerOptionWriteRequest(
    [property: Required, StringLength(TestLimits.AnswerOptionTextMaxLength)] string Text,
    bool IsCorrect,
    int Order) : IApiRequest;

public sealed record AnswerOptionUpdateRequest(
    [property: Required, StringLength(TestLimits.AnswerOptionTextMaxLength)] string Text,
    bool IsCorrect) : IApiRequest;

public sealed record OrderRequest(int Order) : IApiRequest;
public sealed record PublishRequest(Guid IdempotencyKey) : IApiRequest;

public sealed record AssignRequest(
    Guid RevisionId,
    [property: StringLength(ExternalIdentityLimits.MaxIdentifierLength)] string? UserId,
    [property: StringLength(ExternalIdentityLimits.MaxIdentifierLength)] string? GroupId,
    DateTimeOffset AvailableFrom,
    DateTimeOffset? AvailableUntil,
    int? AttemptLimit,
    Guid IdempotencyKey) : IApiRequest;

public sealed record BulkAssignmentTargetRequest(
    [property: StringLength(ExternalIdentityLimits.MaxIdentifierLength)] string? UserId,
    [property: StringLength(ExternalIdentityLimits.MaxIdentifierLength)] string? GroupId) : IApiRequest;

public sealed record BulkAssignRequest(
    Guid RevisionId,
    [property: Required] IReadOnlyCollection<BulkAssignmentTargetRequest> Targets,
    DateTimeOffset AvailableFrom,
    DateTimeOffset? AvailableUntil,
    int? AttemptLimit,
    Guid IdempotencyKey) : IApiRequest;

public sealed record AssignmentWindowRequest(DateTimeOffset AvailableFrom, DateTimeOffset? AvailableUntil) : IApiRequest;
public sealed record AttemptLimitRequest(int? AttemptLimit) : IApiRequest;

public sealed record CancelAssignmentRequest(
    [property: StringLength(TestAssignmentLimits.CancelReasonMaxLength)] string? Reason) : IApiRequest;

public sealed record StartAttemptRequest(Guid IdempotencyKey) : IApiRequest;
public sealed record SubmitAttemptRequest(Guid IdempotencyKey) : IApiRequest;

public sealed record AnswerQuestionRequest(
    [property: Required] IReadOnlyCollection<Guid> OptionIds) : IApiRequest;
