using TestApp.Api;
using TestApp.Application.Queries;
using TestApp.Domain.Attempts;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Api.Endpoints;

internal static class ResultEndpoints
{
    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/results", async (
            Guid? testId,
            Guid? revisionId,
            AttemptOutcome? outcome,
            int? page,
            int? pageSize,
            GetReviewerResultsQueryHandler handler,
            CancellationToken ct) =>
            Results.Ok(await handler.Handle(new GetReviewerResultsQuery(
                testId is null ? null : new TestId(testId.Value),
                revisionId is null ? null : new PublishedTestRevisionId(revisionId.Value),
                outcome,
                page ?? 1,
                pageSize ?? 20), ct)))
            .RequireAuthorization(Permissions.ResultsReview)
            .RequireRateLimiting(RatePolicies.PrivilegedRead)
            .AddEndpointFilter<RequestValidationFilter>();

        endpoints.MapGet("/api/v1/results/{attemptId}", async (
            Guid attemptId,
            GetReviewerAttemptResultQueryHandler handler,
            CancellationToken ct) =>
            await handler.Handle(new GetReviewerAttemptResultQuery(new TestAttemptId(attemptId)), ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound())
            .RequireAuthorization(Permissions.ResultsReview)
            .RequireRateLimiting(RatePolicies.PrivilegedRead);

        return endpoints;
    }
}
