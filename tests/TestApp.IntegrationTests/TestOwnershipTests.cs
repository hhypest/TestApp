using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TestApp.Api;
using TestApp.Application.Queries;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class TestOwnershipTests
{
    [Fact]
    public async Task Authors_are_isolated_while_admin_has_global_scope()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        Authenticate(client, "author-a", "test-author");
        var testA = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Author A test"), ct);

        Authenticate(client, "author-b", "test-author");
        var testB = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Author B test"), ct);
        var questionId = await PostTestValue<QuestionId>(client, testB, $"/api/v1/tests/{testB.Value}/questions",
            new QuestionWriteRequest("Pick correct", QuestionType.SingleChoice, 1m, 1), ct);
        var correctOptionId = await PostTestValue<AnswerOptionId>(client, testB,
            $"/api/v1/tests/{testB.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Correct", true, 1), ct);
        _ = await PostTestValue<AnswerOptionId>(client, testB,
            $"/api/v1/tests/{testB.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Wrong", false, 2), ct);
        var revisionB = await PostTestValue<PublishedTestRevisionId>(client, testB, $"/api/v1/tests/{testB.Value}/publish",
            new PublishRequest(Guid.NewGuid()), ct);

        Authenticate(client, "author-a", "test-author");
        var authorACatalog = await client.GetFromJsonAsync<PagedResult<TestCatalogItem>>("/api/v1/tests?page=1&pageSize=20", ct);
        Assert.NotNull(authorACatalog);
        Assert.Contains(authorACatalog.Items, x => x.Id == testA);
        Assert.DoesNotContain(authorACatalog.Items, x => x.Id == testB);

        using (var editorB = await client.GetAsync($"/api/v1/tests/{testB.Value}/editor", ct))
            Assert.Equal(HttpStatusCode.NotFound, editorB.StatusCode);

        using (var renameB = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tests/{testB.Value}/title")
        {
            Content = JsonContent.Create(new RenameTestRequest("Hijacked"))
        })
        {
            renameB.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
            using var renameResponse = await client.SendAsync(renameB, ct);
            Assert.Equal(HttpStatusCode.Forbidden, renameResponse.StatusCode);
        }

        var foreignRevisions = await client.GetFromJsonAsync<PublishedRevisionSummary[]>($"/api/v1/tests/{testB.Value}/revisions", ct);
        Assert.Empty(Assert.IsType<PublishedRevisionSummary[]>(foreignRevisions));

        Authenticate(client, "admin-1", "test-admin");
        var adminCatalog = await client.GetFromJsonAsync<PagedResult<TestCatalogItem>>("/api/v1/tests?page=1&pageSize=20", ct);
        Assert.NotNull(adminCatalog);
        Assert.Contains(adminCatalog.Items, x => x.Id == testA);
        Assert.Contains(adminCatalog.Items, x => x.Id == testB);

        using (var adminEditorB = await client.GetAsync($"/api/v1/tests/{testB.Value}/editor", ct))
            Assert.Equal(HttpStatusCode.OK, adminEditorB.StatusCode);

        var assignmentId = await PostValue<TestAssignmentId>(client, "/api/v1/assignments", new AssignRequest(
            revisionB.Value,
            "student-1",
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            1,
            Guid.NewGuid()), ct);

        Authenticate(client, "student-1");
        var attemptId = await PostValue<TestAttemptId>(client, $"/api/v1/assignments/{assignmentId.Value}/attempts",
            new StartAttemptRequest(Guid.NewGuid()), ct);
        using (var answer = await client.PutAsJsonAsync(
                   $"/api/v1/attempts/{attemptId.Value}/answers/{questionId.Value}",
                   new AnswerQuestionRequest([correctOptionId.Value]), ct))
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        using (var submit = await client.PostAsJsonAsync($"/api/v1/attempts/{attemptId.Value}/submit",
                   new SubmitAttemptRequest(Guid.NewGuid()), ct))
            Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

        Authenticate(client, "author-a", "test-author");
        var authorAResults = await client.GetFromJsonAsync<PagedResult<ReviewerResultSummary>>(
            $"/api/v1/results?testId={testB.Value}&page=1&pageSize=20", ct);
        Assert.NotNull(authorAResults);
        Assert.Empty(authorAResults.Items);
        using (var foreignDetail = await client.GetAsync($"/api/v1/results/{attemptId.Value}", ct))
            Assert.Equal(HttpStatusCode.NotFound, foreignDetail.StatusCode);

        Authenticate(client, "author-b", "test-author");
        var authorBResults = await client.GetFromJsonAsync<PagedResult<ReviewerResultSummary>>(
            $"/api/v1/results?testId={testB.Value}&page=1&pageSize=20", ct);
        Assert.NotNull(authorBResults);
        Assert.Single(authorBResults.Items);
        var authorBDetail = await client.GetFromJsonAsync<ReviewerAttemptResultView>($"/api/v1/results/{attemptId.Value}", ct);
        Assert.NotNull(authorBDetail);
        Assert.Contains(authorBDetail.Questions.Single().Options, x => x.Id == correctOptionId && x.IsCorrect);

        Authenticate(client, "admin-1", "test-admin");
        var adminDetail = await client.GetFromJsonAsync<ReviewerAttemptResultView>($"/api/v1/results/{attemptId.Value}", ct);
        Assert.NotNull(adminDetail);
        Assert.Equal(testB, adminDetail.TestId);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        ApiTestHost.Create(connectionString);

    private static void Authenticate(HttpClient client, string userId, params string[] roles)
        => ApiTestHost.Authenticate(client, userId, roles);

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
}
