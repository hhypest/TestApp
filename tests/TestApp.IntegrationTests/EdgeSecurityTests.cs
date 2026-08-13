using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TestApp.Api;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class EdgeSecurityTests
{
    [Fact]
    public async Task Untrusted_forwarded_for_is_ignored()
    {
        var options = ForwardedOptions(IPAddress.Parse("10.0.0.10"));
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, loggerFactory, Options.Create(options));

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.25");
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.10";

        await middleware.Invoke(context);

        Assert.Equal(IPAddress.Parse("192.0.2.25"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task Trusted_forwarded_for_is_applied()
    {
        var trustedProxy = IPAddress.Parse("10.0.0.10");
        var options = ForwardedOptions(trustedProxy);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, loggerFactory, Options.Create(options));

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = trustedProxy;
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.10";

        await middleware.Invoke(context);

        Assert.Equal(IPAddress.Parse("198.51.100.10"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task Production_OpenAPI_can_be_enabled_as_admin_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using (var db = database.CreateContext())
            await db.Database.MigrateAsync(ct);

        await using var factory = CreateFactory(database.ConnectionString, builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Keycloak:RequireHttpsMetadata", "true");
            builder.UseSetting("TransportSecurity:HttpsRedirectionEnabled", "false");
            builder.UseSetting("TransportSecurity:HstsEnabled", "false");
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

    [Fact]
    public async Task Operations_rate_limit_is_independent_and_returns_correlation_on_429()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using (var db = database.CreateContext())
            await db.Database.MigrateAsync(ct);

        await using var factory = CreateFactory(database.ConnectionString, builder =>
        {
            builder.UseSetting("RateLimiting:General:PermitLimit", "100");
            builder.UseSetting("RateLimiting:Operations:PermitLimit", "1");
            builder.UseSetting("RateLimiting:Operations:WindowSeconds", "60");
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "admin-rate-limit");
        client.DefaultRequestHeaders.Add("X-Test-Roles", "test-admin");

        using var first = await client.GetAsync("/api/v1/operations/outbox", ct);
        using var second = await client.GetAsync("/api/v1/operations/outbox", ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(second.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task Cors_preflight_allows_only_configured_origin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using (var db = database.CreateContext())
            await db.Database.MigrateAsync(ct);

        await using var factory = CreateFactory(database.ConnectionString, builder =>
        {
            builder.UseSetting("Cors:Enabled", "true");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://frontend.example");
        });
        using var client = factory.CreateClient();

        using var allowedRequest = Preflight("https://frontend.example");
        using var allowed = await client.SendAsync(allowedRequest, ct);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal("https://frontend.example", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var deniedRequest = Preflight("https://evil.example");
        using var denied = await client.SendAsync(deniedRequest, ct);
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Security_headers_are_emitted_when_enabled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using (var db = database.CreateContext())
            await db.Database.MigrateAsync(ct);

        await using var factory = CreateFactory(database.ConnectionString, builder =>
            builder.UseSetting("TransportSecurity:SecurityHeadersEnabled", "true"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/me/attempts");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return request;
    }

    private static ForwardedHeadersOptions ForwardedOptions(IPAddress trustedProxy)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = 1
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Add(trustedProxy);
        return options;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        Action<IWebHostBuilder>? configure = null) =>
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
