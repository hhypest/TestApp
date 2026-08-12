using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class ApiLifecycleContractTests
{
    private static readonly DateTimeOffset DeprecationAt =
        new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SunsetAt =
        new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Legacy_alias_emits_deprecation_and_optional_sunset_headers_only_on_legacy_requests()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString, legacyCompatibilityEnabled: true, SunsetAt);
        using var client = factory.CreateClient();

        using var legacy = await client.GetAsync("/api/me/assignments", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, legacy.StatusCode);
        Assert.Equal($"@{DeprecationAt.ToUnixTimeSeconds()}", GetSingleHeader(legacy, "Deprecation"));
        Assert.Equal(SunsetAt.UtcDateTime.ToString("R", CultureInfo.InvariantCulture), GetSingleHeader(legacy, "Sunset"));

        using var canonical = await client.GetAsync("/api/v1/me/assignments", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, canonical.StatusCode);
        Assert.False(canonical.Headers.Contains("Deprecation"));
        Assert.False(canonical.Headers.Contains("Sunset"));
    }

    [Fact]
    public async Task Disabled_legacy_compatibility_returns_explicit_gone_problem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString, legacyCompatibilityEnabled: false, SunsetAt);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/me/assignments", ct);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal($"@{DeprecationAt.ToUnixTimeSeconds()}", GetSingleHeader(response, "Deprecation"));
        Assert.Equal(SunsetAt.UtcDateTime.ToString("R", CultureInfo.InvariantCulture), GetSingleHeader(response, "Sunset"));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("api.version.retired", document.RootElement.GetProperty("title").GetString());
        Assert.Equal((int)HttpStatusCode.Gone, document.RootElement.GetProperty("status").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    private static string GetSingleHeader(HttpResponseMessage response, string name)
    {
        Assert.True(response.Headers.TryGetValues(name, out var values));
        return Assert.Single(values);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        bool legacyCompatibilityEnabled,
        DateTimeOffset? sunsetAt) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Keycloak:Authority", "https://identity.invalid/realms/testapp");
            builder.UseSetting("Keycloak:Audience", "testapp-api");
            builder.UseSetting("ConnectionStrings:Database", connectionString);
            builder.UseSetting("ApiLifecycle:LegacyCompatibilityEnabled", legacyCompatibilityEnabled.ToString());
            builder.UseSetting("ApiLifecycle:LegacyDeprecationAt", DeprecationAt.ToString("O", CultureInfo.InvariantCulture));
            if (sunsetAt is not null)
                builder.UseSetting("ApiLifecycle:LegacySunsetAt", sunsetAt.Value.ToString("O", CultureInfo.InvariantCulture));
        });
}
