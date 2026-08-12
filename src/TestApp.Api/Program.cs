using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using TestApp.Api;
using TestApp.Application.Assignments;
using TestApp.Application.Attempts;
using TestApp.Application.Common;
using TestApp.Application.Queries;
using TestApp.Application.Tests;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Infrastructure;
using TestApp.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.Authority = builder.Configuration["Keycloak:Authority"];
    o.Audience = builder.Configuration["Keycloak:Audience"];
    o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
});

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Permissions.TestsWrite, p => p.RequireRole("test-author", "test-admin"));
    o.AddPolicy(Permissions.TestsPublish, p => p.RequireRole("test-author", "test-admin"));
    o.AddPolicy(Permissions.TestsAssign, p => p.RequireRole("test-admin"));
    o.AddPolicy(Permissions.ResultsReview, p => p.RequireRole("test-author", "test-admin"));
});

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? "Server=localhost;Port=3306;Database=testapp;User=testapp;Password=testapp;";

builder.Services.AddInfrastructure(o =>
    o.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddScoped<CreateTestCommandHandler>();
builder.Services.AddScoped<RenameTestCommandHandler>();
builder.Services.AddScoped<ChangeTestSettingsCommandHandler>();
builder.Services.AddScoped<AddQuestionCommandHandler>();
builder.Services.AddScoped<UpdateQuestionCommandHandler>();
builder.Services.AddScoped<RemoveQuestionCommandHandler>();
builder.Services.AddScoped<ReorderQuestionCommandHandler>();
builder.Services.AddScoped<AddAnswerOptionCommandHandler>();
builder.Services.AddScoped<UpdateAnswerOptionCommandHandler>();
builder.Services.AddScoped<RemoveAnswerOptionCommandHandler>();
builder.Services.AddScoped<ReorderAnswerOptionCommandHandler>();
builder.Services.AddScoped<ArchiveTestCommandHandler>();
builder.Services.AddScoped<PublishTestCommandHandler>();
builder.Services.AddScoped<AssignTestCommandHandler>();
builder.Services.AddScoped<CancelAssignmentCommandHandler>();
builder.Services.AddScoped<ChangeAssignmentWindowCommandHandler>();
builder.Services.AddScoped<ChangeAssignmentAttemptLimitCommandHandler>();
builder.Services.AddScoped<StartAttemptCommandHandler>();
builder.Services.AddScoped<AnswerQuestionCommandHandler>();
builder.Services.AddScoped<ClearAnswerCommandHandler>();
builder.Services.AddScoped<SubmitAttemptCommandHandler>();
builder.Services.AddScoped<TimeoutAttemptCommandHandler>();
builder.Services.AddScoped<GetTestEditorViewQueryHandler>();
builder.Services.AddScoped<GetMyAssignmentsQueryHandler>();
builder.Services.AddScoped<GetMyAttemptsQueryHandler>();
builder.Services.AddScoped<GetAttemptQueryHandler>();
builder.Services.AddScoped<GetAttemptResultQueryHandler>();
builder.Services.AddScoped<GetReviewerResultsQueryHandler>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

var tests = app.MapGroup("/api/tests").RequireAuthorization();
tests.MapPost("/", async (CreateTestRequest r, CreateTestCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new CreateTestCommand(r.Title), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapPatch("/{id:guid}/title", async (Guid id, RenameTestRequest r, RenameTestCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new RenameTestCommand(new TestId(id), r.Title), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapPatch("/{id:guid}/settings", async (Guid id, TestSettingsRequest r, ChangeTestSettingsCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new ChangeTestSettingsCommand(new TestId(id), r.PassingPercentage, r.TimeLimitMinutes), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapPost("/{id:guid}/questions", async (Guid id, QuestionWriteRequest r, AddQuestionCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new AddQuestionCommand(new TestId(id), r.Text, r.Type, r.Points, r.Order), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapPut("/{id:guid}/questions/{questionId:guid}", async (Guid id, Guid questionId, QuestionUpdateRequest r, UpdateQuestionCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new UpdateQuestionCommand(new TestId(id), new QuestionId(questionId), r.Text, r.Type, r.Points), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapDelete("/{id:guid}/questions/{questionId:guid}", async (Guid id, Guid questionId, RemoveQuestionCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new RemoveQuestionCommand(new TestId(id), new QuestionId(questionId)), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapPatch("/{id:guid}/questions/{questionId:guid}/order", async (Guid id, Guid questionId, OrderRequest r, ReorderQuestionCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new ReorderQuestionCommand(new TestId(id), new QuestionId(questionId), r.Order), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapPost("/{id:guid}/questions/{questionId:guid}/options", async (Guid id, Guid questionId, AnswerOptionWriteRequest r, AddAnswerOptionCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new AddAnswerOptionCommand(new TestId(id), new QuestionId(questionId), r.Text, r.IsCorrect, r.Order), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapPut("/{id:guid}/questions/{questionId:guid}/options/{optionId:guid}", async (Guid id, Guid questionId, Guid optionId, AnswerOptionUpdateRequest r, UpdateAnswerOptionCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new UpdateAnswerOptionCommand(new TestId(id), new QuestionId(questionId), new AnswerOptionId(optionId), r.Text, r.IsCorrect), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapDelete("/{id:guid}/questions/{questionId:guid}/options/{optionId:guid}", async (Guid id, Guid questionId, Guid optionId, RemoveAnswerOptionCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new RemoveAnswerOptionCommand(new TestId(id), new QuestionId(questionId), new AnswerOptionId(optionId)), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapPatch("/{id:guid}/questions/{questionId:guid}/options/{optionId:guid}/order", async (Guid id, Guid questionId, Guid optionId, OrderRequest r, ReorderAnswerOptionCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new ReorderAnswerOptionCommand(new TestId(id), new QuestionId(questionId), new AnswerOptionId(optionId), r.Order), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapPost("/{id:guid}/publish", async (Guid id, PublishRequest r, PublishTestCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new PublishTestCommand(new TestId(id), r.IdempotencyKey), ct))).RequireAuthorization(Permissions.TestsPublish);
tests.MapPost("/{id:guid}/archive", async (Guid id, ArchiveTestCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new ArchiveTestCommand(new TestId(id)), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapGet("/{id:guid}/editor", async (Guid id, GetTestEditorViewQueryHandler h, CancellationToken ct) => await h.Handle(new GetTestEditorViewQuery(new TestId(id)), ct) is { } value ? Results.Ok(value) : Results.NotFound()).RequireAuthorization(Permissions.TestsWrite);

var assignments = app.MapGroup("/api/assignments").RequireAuthorization();
assignments.MapPost("/", async (AssignRequest r, AssignTestCommandHandler h, CancellationToken ct) =>
{
    ExternalUserId? user = string.IsNullOrWhiteSpace(r.UserId) ? null : ExternalUserId.FromSubject(r.UserId);
    ExternalGroupId? group = string.IsNullOrWhiteSpace(r.GroupId) ? null : ExternalGroupId.FromExternalId(r.GroupId);
    return ToHttp(await h.Handle(new AssignTestCommand(new PublishedTestRevisionId(r.RevisionId), user, group, r.AvailableFrom, r.AvailableUntil, r.AttemptLimit, r.IdempotencyKey), ct));
}).RequireAuthorization(Permissions.TestsAssign);
assignments.MapPatch("/{id:guid}/window", async (Guid id, AssignmentWindowRequest r, ChangeAssignmentWindowCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new ChangeAssignmentWindowCommand(new TestAssignmentId(id), r.AvailableFrom, r.AvailableUntil), ct))).RequireAuthorization(Permissions.TestsAssign);
assignments.MapPatch("/{id:guid}/attempt-limit", async (Guid id, AttemptLimitRequest r, ChangeAssignmentAttemptLimitCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new ChangeAssignmentAttemptLimitCommand(new TestAssignmentId(id), r.AttemptLimit), ct))).RequireAuthorization(Permissions.TestsAssign);
assignments.MapPost("/{id:guid}/cancel", async (Guid id, CancelAssignmentRequest r, CancelAssignmentCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new CancelAssignmentCommand(new TestAssignmentId(id), r.Reason), ct))).RequireAuthorization(Permissions.TestsAssign);
assignments.MapPost("/{id:guid}/attempts", async (Guid id, StartAttemptRequest r, StartAttemptCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new StartAttemptCommand(new TestAssignmentId(id), r.IdempotencyKey), ct)));

app.MapGet("/api/me/assignments", async (int? page, int? pageSize, AssignmentStatus? status, GetMyAssignmentsQueryHandler h, CancellationToken ct) =>
    Results.Ok(await h.Handle(new GetMyAssignmentsQuery(page ?? 1, pageSize ?? 20, status), ct))).RequireAuthorization();
app.MapGet("/api/me/attempts", async (int? page, int? pageSize, AttemptStatus? status, GetMyAttemptsQueryHandler h, CancellationToken ct) =>
    Results.Ok(await h.Handle(new GetMyAttemptsQuery(page ?? 1, pageSize ?? 20, status), ct))).RequireAuthorization();
app.MapGet("/api/results", async (Guid? testId, Guid? revisionId, AttemptOutcome? outcome, int? page, int? pageSize, GetReviewerResultsQueryHandler h, CancellationToken ct) =>
    Results.Ok(await h.Handle(new GetReviewerResultsQuery(testId is null ? null : new TestId(testId.Value), revisionId is null ? null : new PublishedTestRevisionId(revisionId.Value), outcome, page ?? 1, pageSize ?? 20), ct))).RequireAuthorization(Permissions.ResultsReview);

var attempts = app.MapGroup("/api/attempts").RequireAuthorization();
attempts.MapPut("/{id:guid}/answers/{questionId:guid}", async (Guid id, Guid questionId, AnswerQuestionRequest r, AnswerQuestionCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new AnswerQuestionCommand(new TestAttemptId(id), new QuestionId(questionId), r.OptionIds.Select(x => new AnswerOptionId(x)).ToArray()), ct)));
attempts.MapDelete("/{id:guid}/answers/{questionId:guid}", async (Guid id, Guid questionId, ClearAnswerCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new ClearAnswerCommand(new TestAttemptId(id), new QuestionId(questionId)), ct)));
attempts.MapPost("/{id:guid}/submit", async (Guid id, SubmitAttemptRequest r, SubmitAttemptCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new SubmitAttemptCommand(new TestAttemptId(id), r.IdempotencyKey), ct)));
attempts.MapPost("/{id:guid}/timeout", async (Guid id, TimeoutAttemptCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new TimeoutAttemptCommand(new TestAttemptId(id)), ct))).RequireAuthorization(Permissions.TestsAssign);
attempts.MapGet("/{id:guid}", async (Guid id, GetAttemptQueryHandler h, CancellationToken ct) => await h.Handle(new GetAttemptQuery(new TestAttemptId(id)), ct) is { } value ? Results.Ok(value) : Results.NotFound());
attempts.MapGet("/{id:guid}/result", async (Guid id, GetAttemptResultQueryHandler h, CancellationToken ct) => await h.Handle(new GetAttemptResultQuery(new TestAttemptId(id)), ct) is { } value ? Results.Ok(value) : Results.NotFound());

app.Run();

static IResult ToHttp<T>(TestApp.Core.Monads.Result<T, Error> result) where T : notnull => result.Match<IResult>(
    value => Results.Ok(value),
    error => Results.Problem(statusCode: error.Type switch { ErrorType.Validation => StatusCodes.Status400BadRequest, ErrorType.NotFound => StatusCodes.Status404NotFound, ErrorType.Conflict => StatusCodes.Status409Conflict, ErrorType.Forbidden => StatusCodes.Status403Forbidden, _ => StatusCodes.Status500InternalServerError }, title: error.Code, detail: error.Message));

public static class Permissions
{
    public const string TestsWrite = "tests:write";
    public const string TestsPublish = "tests:publish";
    public const string TestsAssign = "tests:assign";
    public const string ResultsReview = "results:review";
}

public sealed record CreateTestRequest(string Title);
public sealed record RenameTestRequest(string Title);
public sealed record TestSettingsRequest(decimal PassingPercentage, int? TimeLimitMinutes);
public sealed record QuestionWriteRequest(string Text, QuestionType Type, decimal Points, int Order);
public sealed record QuestionUpdateRequest(string Text, QuestionType Type, decimal Points);
public sealed record AnswerOptionWriteRequest(string Text, bool IsCorrect, int Order);
public sealed record AnswerOptionUpdateRequest(string Text, bool IsCorrect);
public sealed record OrderRequest(int Order);
public sealed record PublishRequest(Guid IdempotencyKey);
public sealed record AssignRequest(Guid RevisionId, string? UserId, string? GroupId, DateTimeOffset AvailableFrom, DateTimeOffset? AvailableUntil, int? AttemptLimit, Guid IdempotencyKey);
public sealed record AssignmentWindowRequest(DateTimeOffset AvailableFrom, DateTimeOffset? AvailableUntil);
public sealed record AttemptLimitRequest(int? AttemptLimit);
public sealed record CancelAssignmentRequest(string? Reason);
public sealed record StartAttemptRequest(Guid IdempotencyKey);
public sealed record SubmitAttemptRequest(Guid IdempotencyKey);
public sealed record AnswerQuestionRequest(IReadOnlyCollection<Guid> OptionIds);

public partial class Program;