using TestApp.Api;
using TestApp.Infrastructure.Outbox;
using TestApp.Infrastructure.Persistence;

namespace TestApp.Api.Endpoints;

internal static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/operations/outbox", async (
            int? deadLetterLimit,
            OutboxMonitor monitor,
            CancellationToken ct) =>
            Results.Ok(await monitor.GetStatusAsync(deadLetterLimit ?? 20, ct)))
            .RequireAuthorization(Permissions.OperationsRead)
            .RequireRateLimiting(RatePolicies.Operations);

        endpoints.MapGet("/api/v1/operations/audit", async (
            string? actorId,
            int? statusCode,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize,
            AuditTrail audit,
            CancellationToken ct) =>
            Results.Ok(await audit.GetAsync(actorId, statusCode, from, to, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization(Permissions.OperationsRead)
            .RequireRateLimiting(RatePolicies.Operations);

        endpoints.MapOutboxDeadLetterEndpoints();

        return endpoints;
    }
}
