using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TestApp.Application.Common;

namespace TestApp.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        if (exception is ConcurrencyConflictException concurrency)
        {
            logger.LogWarning(concurrency, "Optimistic concurrency conflict for {Method} {Path} TraceId={TraceId}",
                httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);

            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "concurrency.conflict",
                Detail = concurrency.Message,
                Instance = httpContext.Request.Path
            };
        }
        else
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path} TraceId={TraceId}",
                httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);

            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "internal.error",
                Detail = "An unexpected error occurred.",
                Instance = httpContext.Request.Path
            };
        }

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
