using TestApp.Api;
using TestApp.Application.Assignments;
using TestApp.Application.Attempts;
using TestApp.Application.Queries;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Api.Endpoints;

internal static class AssignmentEndpoints
{
    public static IEndpointRouteBuilder MapAssignmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var assignments = endpoints.MapGroup("/api/v1/assignments")
            .RequireAuthorization()
            .AddEndpointFilter<RequestValidationFilter>();

        assignments.MapGet("/", async (
            Guid? testId,
            Guid? revisionId,
            AssignmentTargetType? targetType,
            string? targetId,
            AssignmentStatus? status,
            int? page,
            int? pageSize,
            GetAdminAssignmentsQueryHandler handler,
            CancellationToken ct) =>
            Results.Ok(await handler.Handle(new GetAdminAssignmentsQuery(
                testId is null ? null : new TestId(testId.Value),
                revisionId is null ? null : new PublishedTestRevisionId(revisionId.Value),
                targetType,
                targetId,
                status,
                page ?? 1,
                pageSize ?? 20), ct)))
            .RequireAuthorization(Permissions.TestsAssign)
            .RequireRateLimiting(RatePolicies.PrivilegedRead);

        assignments.MapGet("/{id}", async (Guid id, GetAdminAssignmentQueryHandler handler, CancellationToken ct) =>
            await handler.Handle(new GetAdminAssignmentQuery(new TestAssignmentId(id)), ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound())
            .RequireAuthorization(Permissions.TestsAssign)
            .RequireRateLimiting(RatePolicies.PrivilegedRead);

        assignments.MapGet("/{id}/attempts", async (
            Guid id,
            AttemptStatus? status,
            AttemptOutcome? outcome,
            int? page,
            int? pageSize,
            GetAdminAssignmentAttemptsQueryHandler handler,
            CancellationToken ct) =>
            await handler.Handle(new GetAdminAssignmentAttemptsQuery(
                new TestAssignmentId(id),
                status,
                outcome,
                page ?? 1,
                pageSize ?? 20), ct) is { } value
                ? Results.Ok(value)
                : Results.NotFound())
            .RequireAuthorization(Permissions.TestsAssign)
            .RequireRateLimiting(RatePolicies.PrivilegedRead);

        assignments.MapPost("/", async (HttpRequest http, AssignRequest request, AssignTestCommandHandler handler, CancellationToken ct) =>
        {
            var key = IdempotencyKeyResolver.Resolve(http, request.IdempotencyKey);
            if (!key.IsSuccess)
                return key.Error!;

            ExternalUserId? user = string.IsNullOrWhiteSpace(request.UserId)
                ? null
                : ExternalUserId.FromSubject(request.UserId);
            ExternalGroupId? group = string.IsNullOrWhiteSpace(request.GroupId)
                ? null
                : ExternalGroupId.FromExternalId(request.GroupId);

            return ApiResultMapper.ToHttp(await handler.Handle(new AssignTestCommand(
                new PublishedTestRevisionId(request.RevisionId),
                user,
                group,
                request.AvailableFrom,
                request.AvailableUntil,
                request.AttemptLimit,
                key.Value), ct));
        }).RequireAuthorization(Permissions.TestsAssign);

        assignments.MapPost("/bulk", async (HttpRequest http, BulkAssignRequest request, BulkAssignTestsCommandHandler handler, CancellationToken ct) =>
        {
            var key = IdempotencyKeyResolver.Resolve(http, request.IdempotencyKey);
            if (!key.IsSuccess)
                return key.Error!;

            var targets = request.Targets.Select(target => new BulkAssignmentTarget(
                string.IsNullOrWhiteSpace(target.UserId) ? null : ExternalUserId.FromSubject(target.UserId),
                string.IsNullOrWhiteSpace(target.GroupId) ? null : ExternalGroupId.FromExternalId(target.GroupId))).ToArray();

            return ApiResultMapper.ToHttp(await handler.Handle(new BulkAssignTestsCommand(
                new PublishedTestRevisionId(request.RevisionId),
                targets,
                request.AvailableFrom,
                request.AvailableUntil,
                request.AttemptLimit,
                key.Value), ct));
        }).RequireAuthorization(Permissions.TestsAssign);

        assignments.MapPatch("/{id}/window", async (Guid id, AssignmentWindowRequest request, ChangeAssignmentWindowCommandHandler handler, CancellationToken ct) =>
            ApiResultMapper.ToHttp(await handler.Handle(new ChangeAssignmentWindowCommand(
                new TestAssignmentId(id),
                request.AvailableFrom,
                request.AvailableUntil), ct)))
            .RequireAuthorization(Permissions.TestsAssign);

        assignments.MapPatch("/{id}/attempt-limit", async (Guid id, AttemptLimitRequest request, ChangeAssignmentAttemptLimitCommandHandler handler, CancellationToken ct) =>
            ApiResultMapper.ToHttp(await handler.Handle(new ChangeAssignmentAttemptLimitCommand(
                new TestAssignmentId(id),
                request.AttemptLimit), ct)))
            .RequireAuthorization(Permissions.TestsAssign);

        assignments.MapPost("/{id}/cancel", async (Guid id, CancelAssignmentRequest request, CancelAssignmentCommandHandler handler, CancellationToken ct) =>
            ApiResultMapper.ToHttp(await handler.Handle(new CancelAssignmentCommand(
                new TestAssignmentId(id),
                request.Reason), ct)))
            .RequireAuthorization(Permissions.TestsAssign);

        assignments.MapPost("/{id}/attempts", async (Guid id, HttpRequest http, StartAttemptRequest request, StartAttemptCommandHandler handler, CancellationToken ct) =>
        {
            var key = IdempotencyKeyResolver.Resolve(http, request.IdempotencyKey);
            return key.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new StartAttemptCommand(new TestAssignmentId(id), key.Value), ct))
                : key.Error!;
        }).RequireRateLimiting(RatePolicies.StudentWrite);

        endpoints.MapGet("/api/v1/me/assignments", async (
            int? page,
            int? pageSize,
            AssignmentStatus? status,
            GetMyAssignmentsQueryHandler handler,
            CancellationToken ct) =>
            Results.Ok(await handler.Handle(new GetMyAssignmentsQuery(page ?? 1, pageSize ?? 20, status), ct)))
            .RequireAuthorization()
            .AddEndpointFilter<RequestValidationFilter>();

        return endpoints;
    }
}
