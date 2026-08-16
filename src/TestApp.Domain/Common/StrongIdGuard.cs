namespace TestApp.Domain.Common;

/// <summary>
/// Shared guard for the GUID-backed strong identifiers of the model.
/// </summary>
/// <remarks>
/// The guard runs in the identifier constructor, so every explicit construction path — application code,
/// EF Core value converters and <c>System.Text.Json</c> materialisation of the published-revision `jsonb`
/// payload — goes through the same check. See ADR-029 for why the constructor validates instead of a
/// private constructor plus a static factory.
///
/// The one construction path the guard cannot cover is <c>default(TId)</c>/<c>new TId()</c>: C# always
/// keeps the implicit parameterless constructor of a struct reachable. Callers that produce identifiers
/// from untrusted input must therefore still reject the zero GUID at the boundary — the API does this in
/// <c>RequestValidationFilter</c>.
/// </remarks>
internal static class StrongIdGuard
{
    public static Guid Ensure(Guid value, string identifierName, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{identifierName} cannot be an empty GUID.", parameterName);

        return value;
    }
}
