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
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            loggerFactory,
            Options.Create(options));

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
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            loggerFactory,
            Options.Create(options));

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
