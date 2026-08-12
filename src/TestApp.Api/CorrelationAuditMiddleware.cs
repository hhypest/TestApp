using System.Diagnostics;
using System.Security.Claims;
using TestApp.Infrastructure.Persistence;

namespace TestApp.Api;

public sealed class CorrelationAuditMiddleware(
    RequestDelegate next,
    AuditTrail auditTrail,
    ILogger<CorrelationAuditMiddleware> logger)
{
    public const string CorrelationHeader = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;
        context.Response.Headers[CorrelationHeader] = correlationId;
        Activity.Current?.SetTag("testapp.correlation_id", correlationId);

        var started = Stopwatch.GetTimestamp();
        Exception? failure = null;

        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            if (!IsStateChanging(context.Request.Method))
                return;

            var elapsedMs = (decimal)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var traceId = Activity.Current?.TraceId.ToString() ?? correlationId;
            var actorId = context.User.FindFirstValue("sub")
                ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var statusCode = failure is null
                ? context.Response.StatusCode
                : StatusCodes.Status500InternalServerError;

            try
            {
                await auditTrail.RecordAsync(
                    actorId,
                    context.Request.Method,
                    context.Request.Path.Value ?? "/",
                    statusCode,
                    correlationId,
                    traceId,
                    elapsedMs,
                    context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Client disconnect must not mask the original request outcome.
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to persist audit entry for {Method} {Path} CorrelationId={CorrelationId}",
                    context.Request.Method,
                    context.Request.Path,
                    correlationId);
            }
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationHeader, out var values))
        {
            var supplied = values.FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(supplied) && supplied.Length <= 128)
                return supplied;
        }

        return Activity.Current?.TraceId.ToString() ?? Guid.CreateVersion7().ToString("N");
    }

    private static bool IsStateChanging(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
}
