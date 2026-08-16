using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using TestApp.Api;
using TestApp.Application.Queries;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class RequestValidationContractTests
{
    [Fact]
    public async Task Malformed_binding_returns_stable_request_invalid_problem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
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
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
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

    [Fact]
    public async Task Assignment_business_rule_violations_return_domain_error_without_leaking_clr_exception_text()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        Authenticate(client, "author-assignment-validation", "test-author");
        var testId = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Assignment validation"), ct);
        var questionId = await PostTestValue<QuestionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions",
            new QuestionWriteRequest("Pick one", QuestionType.SingleChoice, 1m, 1), ct);
        _ = await PostTestValue<AnswerOptionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Correct", true, 1), ct);
        _ = await PostTestValue<AnswerOptionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Wrong", false, 2), ct);
        var revisionId = await PostTestValue<PublishedTestRevisionId>(client, testId, $"/api/v1/tests/{testId.Value}/publish",
            new PublishRequest(Guid.NewGuid()), ct);

        Authenticate(client, "admin-assignment-validation", "test-admin");
        var now = DateTimeOffset.UtcNow;

        using var invalidWindow = await client.PostAsJsonAsync("/api/v1/assignments", new AssignRequest(
            revisionId.Value, "student-1", null, now, now.AddMinutes(-1), null, Guid.NewGuid()), ct);
        await AssertDomainValidationProblem(invalidWindow, "assignment.window", ct);

        using var invalidAttemptLimit = await client.PostAsJsonAsync("/api/v1/assignments", new AssignRequest(
            revisionId.Value, "student-1", null, now, null, 0, Guid.NewGuid()), ct);
        await AssertDomainValidationProblem(invalidAttemptLimit, "assignment.attempt_limit", ct);
    }

    private static async Task AssertDomainValidationProblem(HttpResponseMessage response, string expectedCode, CancellationToken ct)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal(expectedCode, document.RootElement.GetProperty("title").GetString());
        var detail = document.RootElement.GetProperty("detail").GetString();
        Assert.False(string.IsNullOrWhiteSpace(detail));
        Assert.DoesNotContain("Parameter", detail, StringComparison.Ordinal);
    }

    private static async Task<T> PostValue<T>(HttpClient client, string uri, object body, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(uri, body, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return Assert.IsType<T>(value);
    }

    private static async Task<T> PostTestValue<T>(HttpClient client, TestId testId, string uri, object body, CancellationToken ct)
    {
        using var etagResponse = await client.GetAsync($"/api/v1/tests/{testId.Value}/editor", ct);
        Assert.Equal(HttpStatusCode.OK, etagResponse.StatusCode);
        var etag = etagResponse.Headers.ETag?.ToString()
            ?? throw new Xunit.Sdk.XunitException("Test editor response did not contain ETag.");

        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        using var response = await client.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return Assert.IsType<T>(value);
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
        ApiTestHost.Create(connectionString);

    private static void Authenticate(HttpClient client, string userId, params string[] roles)
        => ApiTestHost.Authenticate(client, userId, roles);
}
