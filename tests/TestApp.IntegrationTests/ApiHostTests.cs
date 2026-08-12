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
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Infrastructure.Outbox;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class ApiHostTests
{
    [Fact]
    public async Task Protected_endpoint_rejects_anonymous_request()
    {
        await using var database = await MariaDbTestDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/me/assignments", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Full_author_publish_assign_attempt_submit_and_review_flow_succeeds()
    {
        await using var database = await MariaDbTestDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        Authenticate(client, "author-1", "test-author");
        var testId = await PostValue<TestId>(client, "/api/tests", new CreateTestRequest("DDD fundamentals"), ct);
        var questionId = await PostValue<QuestionId>(client, $"/api/tests/{testId.Value}/questions", new QuestionWriteRequest("What is an aggregate?", QuestionType.SingleChoice, 1m, 1), ct);
        var correctOptionId = await PostValue<AnswerOptionId>(client, $"/api/tests/{testId.Value}/questions/{questionId.Value}/options", new AnswerOptionWriteRequest("Consistency boundary", true, 1), ct);
        var wrongOptionId = await PostValue<AnswerOptionId>(client, $"/api/tests/{testId.Value}/questions/{questionId.Value}/options", new AnswerOptionWriteRequest("A database table", false, 2), ct);
        var revisionId = await PostValue<PublishedTestRevisionId>(client, $"/api/tests/{testId.Value}/publish", new PublishRequest(Guid.NewGuid()), ct);

        var catalog = await client.GetFromJsonAsync<PagedResult<TestCatalogItem>>("/api/tests?status=Published&search=fundamentals&page=1&pageSize=20", ct);
        Assert.NotNull(catalog);
        var catalogItem = Assert.Single(catalog.Items);
        Assert.Equal(testId, catalogItem.Id);
        Assert.Equal(1, catalogItem.QuestionCount);
        Assert.Equal(1, catalogItem.PublishedRevisionCount);
        Assert.Equal(1, catalogItem.LatestRevisionVersion);

        var revisions = await client.GetFromJsonAsync<PublishedRevisionSummary[]>($"/api/tests/{testId.Value}/revisions", ct);
        var revision = Assert.Single(Assert.IsType<PublishedRevisionSummary[]>(revisions));
        Assert.Equal(revisionId, revision.Id);
        Assert.Equal(1, revision.Version);

        Authenticate(client, "admin-1", "test-admin");
        var assignmentId = await PostValue<TestAssignmentId>(client, "/api/assignments", new AssignRequest(
            revisionId.Value,
            "student-1",
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            1,
            Guid.NewGuid()), ct);

        var outboxStatus = await client.GetFromJsonAsync<OutboxOperationalStatus>("/api/operations/outbox", ct);
        Assert.NotNull(outboxStatus);
        Assert.True(outboxStatus.PendingCount >= 0);

        Authenticate(client, "student-1");
        var attemptId = await PostValue<TestAttemptId>(client, $"/api/assignments/{assignmentId.Value}/attempts", new StartAttemptRequest(Guid.NewGuid()), ct);

        using (var answer = await client.PutAsJsonAsync(
                   $"/api/attempts/{attemptId.Value}/answers/{questionId.Value}",
                   new AnswerQuestionRequest([correctOptionId.Value]),
                   ct))
        {
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        }

        var submitKey = Guid.NewGuid();
        using var firstSubmit = await client.PostAsJsonAsync($"/api/attempts/{attemptId.Value}/submit", new SubmitAttemptRequest(submitKey), ct);
        Assert.Equal(HttpStatusCode.OK, firstSubmit.StatusCode);
        var firstScore = await firstSubmit.Content.ReadFromJsonAsync<AttemptScore>(cancellationToken: ct);
        Assert.NotNull(firstScore);
        Assert.Equal(1m, firstScore.Earned);
        Assert.Equal(1m, firstScore.Maximum);

        using var retrySubmit = await client.PostAsJsonAsync($"/api/attempts/{attemptId.Value}/submit", new SubmitAttemptRequest(submitKey), ct);
        Assert.Equal(HttpStatusCode.OK, retrySubmit.StatusCode);
        var retryScore = await retrySubmit.Content.ReadFromJsonAsync<AttemptScore>(cancellationToken: ct);
        Assert.Equal(firstScore, retryScore);

        var result = await client.GetFromJsonAsync<AttemptResultView>($"/api/attempts/{attemptId.Value}/result", ct);
        Assert.NotNull(result);
        Assert.Equal(AttemptStatus.Submitted, result.Status);
        Assert.Equal(AttemptOutcome.Passed, result.Outcome);
        Assert.Equal(100m, result.Percentage);

        using (var forbiddenReviewerDetail = await client.GetAsync($"/api/results/{attemptId.Value}", ct))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenReviewerDetail.StatusCode);
        using (var forbiddenOperations = await client.GetAsync("/api/operations/outbox", ct))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenOperations.StatusCode);

        Authenticate(client, "author-1", "test-author");
        var reviewed = await client.GetFromJsonAsync<PagedResult<ReviewerResultSummary>>(
            $"/api/results?testId={testId.Value}&outcome={AttemptOutcome.Passed}&page=1&pageSize=20",
            ct);
        Assert.NotNull(reviewed);
        Assert.Single(reviewed.Items);
        Assert.Equal(attemptId, reviewed.Items[0].AttemptId);
        Assert.Equal("student-1", reviewed.Items[0].UserId.Value);

        var detail = await client.GetFromJsonAsync<ReviewerAttemptResultView>($"/api/results/{attemptId.Value}", ct);
        Assert.NotNull(detail);
        Assert.Equal(testId, detail.TestId);
        Assert.Equal(attemptId, detail.AttemptId);
        Assert.Equal("student-1", detail.UserId.Value);
        Assert.Single(detail.Questions);
        var reviewedQuestion = detail.Questions[0];
        Assert.Equal(questionId, reviewedQuestion.Id);
        Assert.Equal(1m, reviewedQuestion.EarnedPoints);
        Assert.Contains(reviewedQuestion.Options, x => x.Id == correctOptionId && x.IsCorrect && x.IsSelected);
        Assert.Contains(reviewedQuestion.Options, x => x.Id == wrongOptionId && !x.IsCorrect && !x.IsSelected);

        using var authorOperations = await client.GetAsync("/api/operations/outbox", ct);
        Assert.Equal(HttpStatusCode.Forbidden, authorOperations.StatusCode);
    }

    [Fact]
    public async Task Author_cannot_use_admin_assignment_endpoint()
    {
        await using var database = await MariaDbTestDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Authenticate(client, "author-1", "test-author");

        using var response = await client.PostAsJsonAsync("/api/assignments", new AssignRequest(
            Guid.NewGuid(), "student-1", null, DateTimeOffset.UtcNow, null, 1, Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string TestScheme = "Test";

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
