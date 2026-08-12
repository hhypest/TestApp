namespace TestApp.Api;

public sealed record ApiLifecycleRuntimeOptions(
    bool LegacyCompatibilityEnabled,
    DateTimeOffset LegacyDeprecationAt,
    DateTimeOffset? LegacySunsetAt);

public static class ApiLifecycleConfiguration
{
    private static readonly DateTimeOffset DefaultLegacyDeprecationAt =
        new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

    public static ApiLifecycleRuntimeOptions Load(IConfiguration configuration)
    {
        var compatibilityEnabled = configuration.GetValue<bool?>("ApiLifecycle:LegacyCompatibilityEnabled") ?? true;
        var deprecationAt = configuration.GetValue<DateTimeOffset?>("ApiLifecycle:LegacyDeprecationAt")
            ?? DefaultLegacyDeprecationAt;
        var sunsetAt = configuration.GetValue<DateTimeOffset?>("ApiLifecycle:LegacySunsetAt");

        if (sunsetAt is not null && sunsetAt < deprecationAt)
            throw new InvalidOperationException(
                "ApiLifecycle:LegacySunsetAt cannot be earlier than ApiLifecycle:LegacyDeprecationAt.");

        return new ApiLifecycleRuntimeOptions(
            compatibilityEnabled,
            deprecationAt.ToUniversalTime(),
            sunsetAt?.ToUniversalTime());
    }
}
