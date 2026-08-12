using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TestApp.Application.Common;

namespace TestApp.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        if (exception is BadHttpRequestException badRequest)
        {
            logger.LogInformation(badRequest,
                "Invalid HTTP request for {Method} {Path} TraceId={TraceId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.TraceIdentifier);

            var statusCode = badRequest.StatusCode is >= 400 and < 500
                ? badRequest.StatusCode
                : StatusCodes.Status400BadRequest;
            httpContext.Response.StatusCode = statusCode;
            problem = new ProblemDetails
            {
                Status = statusCode,
                Title = "request.invalid",
                Detail = "The request could not be parsed or bound to the endpoint contract.",
                Instance = httpContext.Request.Path
            };
        }
        else if (exception is ConcurrencyConflictException concurrency)
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
        else if (exception is IdempotencyKeyReuseException idempotency)
        {
            logger.LogWarning(idempotency,
                "Idempotency key reused with a different request for {Method} {Path} Operation={Operation} TraceId={TraceId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                idempotency.Operation,
                httpContext.TraceIdentifier);

            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "idempotency.key_reused",
                Detail = idempotency.Message,
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
