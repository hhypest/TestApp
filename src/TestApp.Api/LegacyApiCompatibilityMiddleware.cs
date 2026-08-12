using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace TestApp.Api;

public sealed class LegacyApiCompatibilityMiddleware(
    RequestDelegate next,
    ApiLifecycleRuntimeOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsLegacyApiPath(context.Request.Path, out var remaining))
        {
            await next(context);
            return;
        }

        AddLifecycleHeaders(context.Response, options);

        if (!options.LegacyCompatibilityEnabled)
        {
            context.Response.StatusCode = StatusCodes.Status410Gone;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status410Gone,
                Title = "api.version.retired",
                Detail = "The unversioned /api compatibility route has been retired. Use /api/v1.",
                Instance = context.Request.Path,
                Extensions = { ["traceId"] = context.TraceIdentifier }
            });
            return;
        }

        context.Request.Path = "/api/v1" + remaining;
        await next(context);
    }

    internal static bool IsLegacyApiPath(PathString path, out PathString remaining) =>
        path.StartsWithSegments("/api", out remaining) &&
        !path.StartsWithSegments("/api/v1");

    internal static void AddLifecycleHeaders(HttpResponse response, ApiLifecycleRuntimeOptions options)
    {
        response.Headers.Deprecation = $"@{options.LegacyDeprecationAt.ToUnixTimeSeconds()}";
        if (options.LegacySunsetAt is { } sunset)
            response.Headers.Sunset = sunset.UtcDateTime.ToString("R", CultureInfo.InvariantCulture);
    }
}
