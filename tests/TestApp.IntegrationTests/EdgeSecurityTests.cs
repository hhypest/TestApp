using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class EdgeSecurityTests
{
    [Fact]
    public async Task Untrusted_forwarded_for_cannot_create_a_new_rate_limit_partition()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        await using (var db = database.CreateContext())
            await db.Database.MigrateAsync(ct);

        await using var factory = CreateFactory(database.ConnectionString, builder =>
        {
            builder.UseSetting("RateLimiting:PermitLimit", "1");
            builder.UseSetting("RateLimiting:WindowSeconds", "60");
            builder.UseSetting("ReverseProxy:Enabled", "true");
            builder.UseSetting("ReverseProxy:KnownProxies:0", "10.0.0.10");
        });
        using var client = factory.CreateClient();

        using var first = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        first.Headers.Add("X-Forwarded-For", "198.51.100.10");
        using var firstResponse = await client.SendAsync(first, ct);

        using var second = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        second.Headers.Add("X-Forwarded-For", "203.0.113.20");
        using var secondResponse = await client.SendAsync(second, ct);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
        Assert.True(secondResponse.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task Production_OpenAPI_can_be_enabled_as_admin_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        await using (var db = database.CreateContext())
            await db.Database.MigrateAsync(ct);

        await using var factory = CreateFactory(database.ConnectionString, builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Keycloak:RequireHttpsMetadata", "true");
            builder.UseSetting("OpenApi:Enabled", "true");
            builder.UseSetting("OpenApi:AllowAnonymous", "false");
        });
        using var client = factory.CreateClient();

        using var anonymous = await client.GetAsync("/openapi/v1.json", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        client.DefaultRequestHeaders.Add("X-Test-User", "admin-1");
        client.DefaultRequestHeaders.Add("X-Test-Roles", "test-admin");
        using var admin = await client.GetAsync("/openapi/v1.json", ct);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", connectionString);
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("Keycloak:Authority", "https://identity.invalid/realms/testapp");
            builder.UseSetting("Keycloak:Audience", "testapp-api");
            configure?.Invoke(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = BoundaryAuthenticationHandler.TestScheme;
                        options.DefaultChallengeScheme = BoundaryAuthenticationHandler.TestScheme;
                        options.DefaultScheme = BoundaryAuthenticationHandler.TestScheme;
                    })
                    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BoundaryAuthenticationHandler>(
                        BoundaryAuthenticationHandler.TestScheme,
                        _ => { });
            });
        });
}
