using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TestApp.Api;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class IdempotencyHttpContractTests
{
    [Fact]
    public async Task Header_only_publish_replays_same_result()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Authenticate(client, "author-header", "test-author");

        var testId = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Header publish"), ct);
        var questionId = await PostTestValue<QuestionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions",
            new QuestionWriteRequest("Pick one", QuestionType.SingleChoice, 1m, 1), ct);
        _ = await PostTestValue<AnswerOptionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Correct", true, 1), ct);
        _ = await PostTestValue<AnswerOptionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Wrong", false, 2), ct);

        var key = Guid.NewGuid();
        var first = await PublishWithHeader(client, testId, key, Guid.Empty, ct);
        var second = await PublishWithHeader(client, testId, key, Guid.Empty, ct);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Different_header_and_body_keys_are_rejected_before_use_case()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Authenticate(client, "author-mismatch", "test-author");

        var testId = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Mismatch"), ct);
        var etag = await GetTestEtagAsync(client, testId, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tests/{testId.Value}/publish")
        {
            Content = JsonContent.Create(new PublishRequest(Guid.NewGuid()))
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add(IdempotencyKeyResolver.HeaderName, Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: ct);
        Assert.Equal("idempotency.key_mismatch", problem?.Title);
    }

    [Fact]
    public async Task Reusing_assignment_key_with_different_payload_returns_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        Authenticate(client, "author-fingerprint", "test-author");
        var testId = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Fingerprint assignment"), ct);
        var questionId = await PostTestValue<QuestionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions",
            new QuestionWriteRequest("Pick", QuestionType.SingleChoice, 1m, 1), ct);
        _ = await PostTestValue<AnswerOptionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("A", true, 1), ct);
        _ = await PostTestValue<AnswerOptionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("B", false, 2), ct);
        var revision = await PublishWithHeader(client, testId, Guid.NewGuid(), Guid.Empty, ct);

        Authenticate(client, "admin-fingerprint", "test-admin");
        var key = Guid.NewGuid();
        var availableFrom = DateTimeOffset.UtcNow.AddMinutes(-1);
        var availableUntil = DateTimeOffset.UtcNow.AddHours(1);

        using var firstRequest = AssignmentRequest(revision, "student-a", key, availableFrom, availableUntil);
        using var first = await client.SendAsync(firstRequest, ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var changedRequest = AssignmentRequest(revision, "student-b", key, availableFrom, availableUntil);
        using var changed = await client.SendAsync(changedRequest, ct);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var problem = await changed.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: ct);
        Assert.Equal("idempotency.key_reused", problem?.Title);
    }

    private static HttpRequestMessage AssignmentRequest(
        PublishedTestRevisionId revision,
        string userId,
        Guid key,
        DateTimeOffset availableFrom,
        DateTimeOffset availableUntil)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/assignments")
        {
            Content = JsonContent.Create(new AssignRequest(
                revision.Value,
                userId,
                null,
                availableFrom,
                availableUntil,
                1,
                Guid.Empty))
        };
        request.Headers.Add(IdempotencyKeyResolver.HeaderName, key.ToString());
        return request;
    }

    private static async Task<PublishedTestRevisionId> PublishWithHeader(
        HttpClient client,
        TestId testId,
        Guid headerKey,
        Guid bodyKey,
        CancellationToken ct)
    {
        var etag = await GetTestEtagAsync(client, testId, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tests/{testId.Value}/publish")
        {
            Content = JsonContent.Create(new PublishRequest(bodyKey))
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add(IdempotencyKeyResolver.HeaderName, headerKey.ToString());
        using var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<PublishedTestRevisionId>(cancellationToken: ct);
        return Assert.IsType<PublishedTestRevisionId>(value);
    }

    private static async Task<T> PostTestValue<T>(HttpClient client, TestId testId, string uri, object body, CancellationToken ct)
    {
        var etag = await GetTestEtagAsync(client, testId, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        using var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return Assert.IsType<T>(value);
    }

    private static async Task<string> GetTestEtagAsync(HttpClient client, TestId testId, CancellationToken ct)
    {
        using var response = await client.GetAsync($"/api/v1/tests/{testId.Value}/editor", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.ETag?.ToString()
            ?? throw new Xunit.Sdk.XunitException("Test editor response did not contain ETag.");
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

    private static async Task<T> PostValue<T>(HttpClient client, string uri, object body, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(uri, body, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return Assert.IsType<T>(value);
    }
}
