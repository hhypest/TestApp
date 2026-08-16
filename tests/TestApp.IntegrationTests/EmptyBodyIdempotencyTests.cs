using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TestApp.Api;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class EmptyBodyIdempotencyTests
{
    [Fact]
    public async Task Publish_accepts_a_true_zero_length_body_with_header_only_idempotency_key()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Authenticate(client, "author-empty-body", "test-author");

        var testId = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Empty body publish"), ct);
        var questionId = await PostTestValue<QuestionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions",
            new QuestionWriteRequest("Pick one", QuestionType.SingleChoice, 1m, 1), ct);
        _ = await PostTestValue<AnswerOptionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Correct", true, 1), ct);
        _ = await PostTestValue<AnswerOptionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Wrong", false, 2), ct);

        var etag = await GetTestEtagAsync(client, testId, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tests/{testId.Value}/publish");
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add(IdempotencyKeyResolver.HeaderName, Guid.NewGuid().ToString());
        Assert.Null(request.Content);

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var revisionId = await response.Content.ReadFromJsonAsync<PublishedTestRevisionId>(cancellationToken: ct);
        Assert.NotEqual(default, revisionId);
    }

    [Fact]
    public async Task Start_attempt_and_submit_accept_a_true_zero_length_body_with_header_only_idempotency_key()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Authenticate(client, "author-empty-body-2", "test-author");

        var testId = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Empty body attempt"), ct);
        var questionId = await PostTestValue<QuestionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions",
            new QuestionWriteRequest("Pick one", QuestionType.SingleChoice, 1m, 1), ct);
        var correctOptionId = await PostTestValue<AnswerOptionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Correct", true, 1), ct);
        _ = await PostTestValue<AnswerOptionId>(client, testId, $"/api/v1/tests/{testId.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Wrong", false, 2), ct);

        var etag = await GetTestEtagAsync(client, testId, ct);
        using var publishRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tests/{testId.Value}/publish");
        publishRequest.Headers.TryAddWithoutValidation("If-Match", etag);
        publishRequest.Headers.Add(IdempotencyKeyResolver.HeaderName, Guid.NewGuid().ToString());
        using var publishResponse = await client.SendAsync(publishRequest, ct);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var revisionId = await publishResponse.Content.ReadFromJsonAsync<PublishedTestRevisionId>(cancellationToken: ct);

        Authenticate(client, "admin-empty-body", "test-admin");
        var assignmentId = await PostValue<TestAssignmentId>(client, "/api/v1/assignments", new AssignRequest(
            revisionId.Value,
            "student-empty-body",
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            1,
            Guid.NewGuid()), ct);

        Authenticate(client, "student-empty-body");
        using var startRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/assignments/{assignmentId.Value}/attempts");
        startRequest.Headers.Add(IdempotencyKeyResolver.HeaderName, Guid.NewGuid().ToString());
        Assert.Null(startRequest.Content);
        using var startResponse = await client.SendAsync(startRequest, ct);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var attemptId = await startResponse.Content.ReadFromJsonAsync<TestAttemptId>(cancellationToken: ct);

        using (var answer = await client.PutAsJsonAsync(
                   $"/api/v1/attempts/{attemptId.Value}/answers/{questionId.Value}",
                   new AnswerQuestionRequest([correctOptionId.Value]),
                   ct))
        {
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        }

        using var submitRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/attempts/{attemptId.Value}/submit");
        submitRequest.Headers.Add(IdempotencyKeyResolver.HeaderName, Guid.NewGuid().ToString());
        Assert.Null(submitRequest.Content);
        using var submitResponse = await client.SendAsync(submitRequest, ct);

        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var score = await submitResponse.Content.ReadFromJsonAsync<AttemptScore>(cancellationToken: ct);
        Assert.NotNull(score);
        Assert.Equal(1m, score.Earned);
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
        var etag = await GetTestEtagAsync(client, testId, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
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
        ApiTestHost.Create(connectionString);

    private static void Authenticate(HttpClient client, string userId, params string[] roles)
        => ApiTestHost.Authenticate(client, userId, roles);
}
