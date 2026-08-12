using System.ComponentModel.DataAnnotations;
using TestApp.Application.Abstractions;
using TestApp.Infrastructure.Outbox;

namespace TestApp.Api;

public static class OutboxDeadLetterEndpoints
{
    public static IEndpointRouteBuilder MapOutboxDeadLetterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/operations/outbox/dead-letters")
            .RequireAuthorization(Permissions.OperationsRead)
            .RequireRateLimiting(RatePolicies.Operations)
            .AddEndpointFilter<RequestValidationFilter>();

        group.MapGet("/{eventId}", async (
            Guid eventId,
            OutboxDeadLetterManager manager,
            CancellationToken ct) =>
        {
            var detail = await manager.GetAsync(eventId, ct);
            return detail is null
                ? Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "outbox.dead_letter.not_found",
                    detail: "The dead-lettered Outbox message was not found.")
                : Results.Ok(detail);
        });

        group.MapPost("/{eventId}/requeue", async (
            Guid eventId,
            DeadLetterActionRequest request,
            ICurrentActor actor,
            HttpContext http,
            OutboxDeadLetterManager manager,
            CancellationToken ct) =>
            ToHttp(await manager.RequeueAsync(
                eventId,
                actor.UserId,
                request.Reason,
                http.TraceIdentifier,
                ct)));

        group.MapPost("/{eventId}/discard", async (
            Guid eventId,
            DeadLetterActionRequest request,
            ICurrentActor actor,
            HttpContext http,
            OutboxDeadLetterManager manager,
            CancellationToken ct) =>
            ToHttp(await manager.DiscardAsync(
                eventId,
                actor.UserId,
                request.Reason,
                http.TraceIdentifier,
                ct)));

        return endpoints;
    }

    private static IResult ToHttp(OutboxDeadLetterCommandResult result) => result.Status switch
    {
        OutboxDeadLetterCommandStatus.Success => Results.Ok(result.Detail),
        OutboxDeadLetterCommandStatus.NotFound => Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: result.Code,
            detail: result.Message),
        OutboxDeadLetterCommandStatus.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: result.Code,
            detail: result.Message),
        OutboxDeadLetterCommandStatus.Busy => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: result.Code,
            detail: result.Message),
        _ => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "internal.error",
            detail: "An unexpected error occurred.")
    };
}

public sealed record DeadLetterActionRequest(
    [property: Required, StringLength(
        OutboxDeadLetterActionLimits.ReasonMaxLength,
        MinimumLength = 1)]
    string Reason) : IApiRequest;
