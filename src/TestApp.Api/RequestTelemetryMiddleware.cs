using System.Diagnostics;

namespace TestApp.Api;

public sealed class RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:F1} ms TraceId={TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                elapsedMs,
                Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
        }
    }
}
