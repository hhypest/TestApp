using Microsoft.Net.Http.Headers;

namespace TestApp.Api;

public readonly record struct ConcurrencyPreconditionResolution(long Value, IResult? Error)
{
    public bool IsSuccess => Error is null;
}

public static class TestEtags
{
    public static string Format(long concurrencyVersion) => $"\"{concurrencyVersion}\"";

    public static ConcurrencyPreconditionResolution ResolveRequiredIfMatch(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(HeaderNames.IfMatch, out var values) || values.Count == 0)
        {
            return Failure(
                StatusCodes.Status428PreconditionRequired,
                "concurrency.precondition_required",
                "If-Match is required when modifying an existing test. Read the editor resource and use its ETag.");
        }

        if (values.Count != 1)
            return Failure(StatusCodes.Status400BadRequest, "concurrency.if_match", "If-Match must contain exactly one strong ETag.");

        var value = values[0]?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value == "*" || value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            return Failure(StatusCodes.Status400BadRequest, "concurrency.if_match", "If-Match must contain one strong numeric ETag; wildcard and weak validators are not supported.");

        if (value.Length < 3 || value[0] != '"' || value[^1] != '"' ||
            !long.TryParse(value[1..^1], out var version) || version < 0)
            return Failure(StatusCodes.Status400BadRequest, "concurrency.if_match", "If-Match must use the ETag returned by the test editor endpoint.");

        return new ConcurrencyPreconditionResolution(version, null);
    }

    private static ConcurrencyPreconditionResolution Failure(int statusCode, string code, string detail) =>
        new(0, Results.Problem(statusCode: statusCode, title: code, detail: detail));
}
