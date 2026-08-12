using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
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
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        Authenticate(client, "author-a", "test-author");
        var testA = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Author A test"), ct);

        Authenticate(client, "author-b", "test-author");
        var testB = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Author B test"), ct);
        var questionId = await PostValue<QuestionId>(client, $"/api/v1/tests/{testB.Value}/questions",
            new QuestionWriteRequest("Pick correct", QuestionType.SingleChoice, 1m, 1), ct);
        var correctOptionId = await PostValue<AnswerOptionId>(client,
            $"/api/v1/tests/{testB.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Correct", true, 1), ct);
        _ = await PostValue<AnswerOptionId>(client,
            $"/api/v1/tests/{testB.Value}/questions/{questionId.Value}/options",
            new AnswerOptionWriteRequest("Wrong", false, 2), ct);
        var revisionB = await PostValue<PublishedTestRevisionId>(client, $"/api/v1/tests/{testB.Value}/publish",
            new PublishRequest(Guid.NewGuid()), ct);

        Authenticate(client, "author-a", "test-author");
        var authorACatalog = await client.GetFromJsonAsync<PagedResult<TestCatalogItem>>("/api/v1/tests?page=1&pageSize=20", ct);
        Assert.NotNull(authorACatalog);
        Assert.Contains(authorACatalog.Items, x => x.Id == testA);
        Assert.DoesNotContain(authorACatalog.Items, x => x.Id == testB);

        using (var editorB = await client.GetAsync($"/api/v1/tests/{testB.Value}/editor", ct))
            Assert.Equal(HttpStatusCode.NotFound, editorB.StatusCode);

        using (var renameB = await client.PatchAsJsonAsync($"/api/v1/tests/{testB.Value}/title", new RenameTestRequest("Hijacked"), ct))
            Assert.Equal(HttpStatusCode.Forbidden, renameB.StatusCode);

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
