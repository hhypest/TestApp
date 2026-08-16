using TestApp.Api;
using TestApp.Application.Attempts;
using TestApp.Application.Queries;
using TestApp.Domain.Attempts;
using TestApp.Domain.Tests;

namespace TestApp.Api.Endpoints;

internal static class AttemptEndpoints
{
    public static IEndpointRouteBuilder MapAttemptEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me/attempts", async (
            int? page,
            int? pageSize,
            AttemptStatus? status,
            GetMyAttemptsQueryHandler handler,
            CancellationToken ct) =>
            Results.Ok(await handler.Handle(new GetMyAttemptsQuery(page ?? 1, pageSize ?? 20, status), ct)))
            .RequireAuthorization()
            .AddEndpointFilter<RequestValidationFilter>();

        var attempts = endpoints.MapGroup("/api/v1/attempts")
            .RequireAuthorization()
            .AddEndpointFilter<RequestValidationFilter>();

        attempts.MapPut("/{id}/answers/{questionId}", async (
            Guid id,
            Guid questionId,
            AnswerQuestionRequest request,
            AnswerQuestionCommandHandler handler,
            CancellationToken ct) =>
            ApiResultMapper.ToHttp(await handler.Handle(new AnswerQuestionCommand(
                new TestAttemptId(id),
                new QuestionId(questionId),
                request.OptionIds.Select(optionId => new AnswerOptionId(optionId)).ToArray()), ct)))
            .RequireRateLimiting(RatePolicies.StudentWrite);

        attempts.MapDelete("/{id}/answers/{questionId}", async (
            Guid id,
            Guid questionId,
            ClearAnswerCommandHandler handler,
            CancellationToken ct) =>
            ApiResultMapper.ToHttp(await handler.Handle(new ClearAnswerCommand(
                new TestAttemptId(id),
                new QuestionId(questionId)), ct)))
            .RequireRateLimiting(RatePolicies.StudentWrite);

        attempts.MapPost("/{id}/submit", async (
            Guid id,
            HttpRequest http,
            SubmitAttemptRequest? request,
            SubmitAttemptCommandHandler handler,
            CancellationToken ct) =>
        {
            var key = IdempotencyKeyResolver.Resolve(http, request?.IdempotencyKey ?? Guid.Empty);
            return key.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new SubmitAttemptCommand(new TestAttemptId(id), key.Value), ct))
                : key.Error!;
        }).RequireRateLimiting(RatePolicies.StudentWrite);

        attempts.MapPost("/{id}/timeout", async (Guid id, TimeoutAttemptCommandHandler handler, CancellationToken ct) =>
            ApiResultMapper.ToHttp(await handler.Handle(new TimeoutAttemptCommand(new TestAttemptId(id)), ct)))
            .RequireAuthorization(Permissions.TestsAssign);

        attempts.MapGet("/{id}", async (Guid id, GetAttemptQueryHandler handler, CancellationToken ct) =>
            await handler.Handle(new GetAttemptQuery(new TestAttemptId(id)), ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound());

        // Student-safe question/option presentation for taking or resuming this attempt
        // (ATT-010/UX-001). Deliberately a separate resource rather than a widened
        // GET /attempts/{id}, so the existing v1 response shape stays unchanged.
        attempts.MapGet("/{id}/presentation", async (
            Guid id,
            GetAttemptPresentationQueryHandler handler,
            CancellationToken ct) =>
            await handler.Handle(new GetAttemptPresentationQuery(new TestAttemptId(id)), ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound());

        attempts.MapGet("/{id}/result", async (Guid id, GetAttemptResultQueryHandler handler, CancellationToken ct) =>
            await handler.Handle(new GetAttemptResultQuery(new TestAttemptId(id)), ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound());

        return endpoints;
    }
}
