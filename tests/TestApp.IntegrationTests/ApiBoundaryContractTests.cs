using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TestApp.Application.Queries;
using TestApp.Domain.Tests;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class ApiBoundaryContractTests
{
    [Fact]
    public async Task Canonical_and_legacy_routes_share_auth_contract_and_OpenAPI_documents_only_v1()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var canonical = await client.GetAsync("/api/v1/me/assignments", ct);
        using var legacy = await client.GetAsync("/api/me/assignments", ct);
        using var openApi = await client.GetAsync("/openapi/v1.json", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, canonical.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, legacy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);

        var document = await openApi.Content.ReadAsStringAsync(ct);
        Assert.Contains("\"/api/v1/tests\"", document, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/api/tests\"", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task State_changing_request_echoes_correlation_id_and_is_queryable_in_audit_trail()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        Authenticate(client, "author-1", "test-author");
        var correlationId = $"contract-{Guid.CreateVersion7():N}";
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tests")
        {
            Content = JsonContent.Create(new CreateTestRequest("Audit contract"))
        };
        createRequest.Headers.Add("X-Correlation-ID", correlationId);

        using var createResponse = await client.SendAsync(createRequest, ct);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.True(createResponse.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Equal(correlationId, Assert.Single(values));
        _ = await createResponse.Content.ReadFromJsonAsync<TestId>(cancellationToken: ct);

        Authenticate(client, "admin-1", "test-admin");
        var audit = await client.GetFromJsonAsync<PagedResult<AuditEntryView>>(
            "/api/v1/operations/audit?actorId=author-1&page=1&pageSize=20",
            ct);

        Assert.NotNull(audit);
        var entry = Assert.Single(audit.Items, x => x.CorrelationId == correlationId);
        Assert.Equal("author-1", entry.ActorId);
        Assert.Equal("POST", entry.Method);
        Assert.Equal("/api/v1/tests", entry.Route);
        Assert.Equal((int)HttpStatusCode.OK, entry.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(entry.TraceId));
        Assert.True(entry.DurationMs >= 0m);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Keycloak:Authority", "https://identity.invalid/realms/testapp");
            builder.UseSetting("Keycloak:Audience", "testapp-api");
            builder.UseSetting("ConnectionStrings:Database", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = BoundaryAuthenticationHandler.TestScheme;
                        options.DefaultChallengeScheme = BoundaryAuthenticationHandler.TestScheme;
                        options.DefaultScheme = BoundaryAuthenticationHandler.TestScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, BoundaryAuthenticationHandler>(BoundaryAuthenticationHandler.TestScheme, _ => { });
            });
        });

    private static void Authenticate(HttpClient client, string userId, params string[] roles)
    {
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Remove("X-Test-Roles");
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        if (roles.Length > 0)
            client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
    }
}

internal sealed class BoundaryAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string TestScheme = "BoundaryTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var userValues) || string.IsNullOrWhiteSpace(userValues.FirstOrDefault()))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("sub", userValues.First()!) };
        if (Request.Headers.TryGetValue("X-Test-Roles", out var roleValues))
        {
            foreach (var role in roleValues.SelectMany(x => x?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []))
                claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, TestScheme, "sub", ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, TestScheme)));
    }
}
