using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using TestApp.Api;
using TestApp.Application.Queries;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class ConcurrencyHttpContractTests
{
    [Fact]
    public async Task Editor_ETag_tracks_version_and_stale_write_returns_412()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Authenticate(client, "author-etag", "test-author");

        var testId = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Concurrency test"), ct);
        var initial = await ReadEditorAsync(client, testId, ct);

        Assert.Equal(TestEtags.Format(initial.Editor.ConcurrencyVersion), initial.ETag);

        using (var missing = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tests/{testId.Value}/title")
        {
            Content = JsonContent.Create(new RenameTestRequest("Missing precondition"))
        })
        using (var missingResponse = await client.SendAsync(missing, ct))
        {
            Assert.Equal((HttpStatusCode)428, missingResponse.StatusCode);
            var problem = await missingResponse.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: ct);
            Assert.Equal("concurrency.precondition_required", problem?.Title);
        }

        using (var valid = RenameRequest(testId, "Updated", initial.ETag))
        using (var validResponse = await client.SendAsync(valid, ct))
            Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

        var updated = await ReadEditorAsync(client, testId, ct);
        Assert.NotEqual(initial.ETag, updated.ETag);
        Assert.True(updated.Editor.ConcurrencyVersion > initial.Editor.ConcurrencyVersion);
        Assert.Equal("Updated", updated.Editor.Title);

        using (var stale = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tests/{testId.Value}/settings")
        {
            Content = JsonContent.Create(new TestSettingsRequest(80m, 30))
        })
        {
            stale.Headers.TryAddWithoutValidation("If-Match", initial.ETag);
            using var staleResponse = await client.SendAsync(stale, ct);
            Assert.Equal(HttpStatusCode.PreconditionFailed, staleResponse.StatusCode);
            var problem = await staleResponse.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: ct);
            Assert.Equal("concurrency.precondition_failed", problem?.Title);
        }
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"0\"")]
    [InlineData("not-an-etag")]
    public async Task Unsupported_IfMatch_validator_returns_400(string ifMatch)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Authenticate(client, "author-invalid-etag", "test-author");

        var testId = await PostValue<TestId>(client, "/api/v1/tests", new CreateTestRequest("Invalid ETag"), ct);
        using var request = RenameRequest(testId, "Rejected", ifMatch);
        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: ct);
        Assert.Equal("concurrency.if_match", problem?.Title);
    }

    private static HttpRequestMessage RenameRequest(TestId testId, string title, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tests/{testId.Value}/title")
        {
            Content = JsonContent.Create(new RenameTestRequest(title))
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return request;
    }

    private static async Task<(TestEditorView Editor, string ETag)> ReadEditorAsync(
        HttpClient client,
        TestId testId,
        CancellationToken ct)
    {
        using var response = await client.GetAsync($"/api/v1/tests/{testId.Value}/editor", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var editor = await response.Content.ReadFromJsonAsync<TestEditorView>(cancellationToken: ct);
        Assert.NotNull(editor);
        var etag = response.Headers.ETag?.ToString()
            ?? throw new Xunit.Sdk.XunitException("Editor response did not contain ETag.");
        return (editor, etag);
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
}
