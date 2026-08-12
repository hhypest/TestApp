namespace TestApp.Api;

public readonly record struct IdempotencyKeyResolution(Guid Value, IResult? Error)
{
    public bool IsSuccess => Error is null;
}

public static class IdempotencyKeyResolver
{
    public const string HeaderName = "Idempotency-Key";

    public static IdempotencyKeyResolution Resolve(HttpRequest request, Guid bodyKey)
    {
        if (request.Headers.TryGetValue(HeaderName, out var values))
        {
            if (values.Count != 1 || !Guid.TryParse(values[0], out var headerKey) || headerKey == Guid.Empty)
                return Failure("idempotency.key", $"{HeaderName} must contain exactly one non-empty UUID value.");

            if (bodyKey != Guid.Empty && bodyKey != headerKey)
                return Failure(
                    "idempotency.key_mismatch",
                    $"{HeaderName} and the legacy body idempotencyKey must match when both are supplied.");

            return new IdempotencyKeyResolution(headerKey, null);
        }

        if (bodyKey == Guid.Empty)
            return Failure(
                "idempotency.key",
                $"{HeaderName} is required. The legacy body idempotencyKey remains temporarily supported for compatibility.");

        return new IdempotencyKeyResolution(bodyKey, null);
    }

    private static IdempotencyKeyResolution Failure(string code, string detail) =>
        new(Guid.Empty, Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: code,
            detail: detail));
}
