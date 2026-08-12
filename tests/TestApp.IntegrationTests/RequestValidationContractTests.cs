using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TestApp.Application.Queries;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class RequestValidationContractTests
{
    [Fact]
    public async Task Malformed_binding_returns_stable_request_invalid_problem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Authenticate(client, "author-validation", "test-author");

        using (var malformedRoute = await client.GetAsync("/api/v1/tests/not-a-guid/editor", ct))
            await AssertProblem(malformedRoute, "request.invalid", ct);

        using var malformedJsonRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tests")
        {
            Content = new StringContent("{\"title\":", Encoding.UTF8, "application/json")
        };
        using (var malformedJson = await client.SendAsync(malformedJsonRequest, ct))
            await AssertProblem(malformedJson, "request.invalid", ct);

        using (var malformedQuery = await client.GetAsync("/api/v1/tests?status=not-a-status", ct))
            await AssertProblem(malformedQuery, "request.invalid", ct);
    }

    [Fact]
    public async Task Semantic_transport_validation_is_stable_and_does_not_mutate_state()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Authenticate(client, "author-validation", "test-author");

        using (var undefinedEnum = await client.GetAsync("/api/v1/tests?status=999", ct))
            await AssertValidationProblem(undefinedEnum, ct);

        using (var missingTitle = await client.PostAsJsonAsync(
                   "/api/v1/tests",
                   new { title = (string?)null },
                   ct))
            await AssertValidationProblem(missingTitle, ct);

        using (var oversizedTitle = await client.PostAsJsonAsync(
                   "/api/v1/tests",
                   new CreateTestRequest(new string('x', TestLimits.TitleMaxLength + 1)),
                   ct))
            await AssertValidationProblem(oversizedTitle, ct);

        var catalogAfterInvalidRequests = await client.GetFromJsonAsync<PagedResult<TestCatalogItem>>(
            "/api/v1/tests?page=1&pageSize=20",
            ct);
        Assert.NotNull(catalogAfterInvalidRequests);
        Assert.Equal(0, catalogAfterInvalidRequests.TotalCount);

        using var create = await client.PostAsJsonAsync(
            "/api/v1/tests",
            new CreateTestRequest("Validation test"),
            ct);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var testId = await create.Content.ReadFromJsonAsync<TestId>(cancellationToken: ct);

        using var invalidQuestionRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tests/{testId.Value}/questions")
        {
            Content = JsonContent.Create(new
            {
                text = "Unsupported type",
                type = 999,
                points = 1m,
                order = 1
            })
        };
        using var invalidQuestion = await client.SendAsync(invalidQuestionRequest, ct);
        await AssertValidationProblem(invalidQuestion, ct);
    }

    private static async Task AssertValidationProblem(HttpResponseMessage response, CancellationToken ct)
    {
        await AssertProblem(response, "request.validation", ct);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.True(document.RootElement.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.NotEmpty(errors.EnumerateObject());
    }

    private static async Task AssertProblem(HttpResponseMessage response, string expectedTitle, CancellationToken ct)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal(expectedTitle, document.RootElement.GetProperty("title").GetString());
        Assert.Equal(StatusCodes.Status400BadRequest, document.RootElement.GetProperty("status").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
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
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.TestScheme;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.TestScheme;
                        options.DefaultScheme = TestAuthenticationHandler.TestScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.TestScheme, _ => { });
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
