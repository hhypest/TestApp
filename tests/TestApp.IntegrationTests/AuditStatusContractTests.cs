using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TestApp.Api;
using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Domain.Tests;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class AuditStatusContractTests
{
    [Fact]
    public async Task Handled_bad_request_is_audited_with_final_400_status()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Authenticate(client);

        var correlationId = NewCorrelationId("bad-request");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tests")
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        };
        request.Headers.Add(CorrelationAuditMiddleware.CorrelationHeader, correlationId);

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: ct);
        Assert.Equal("request.invalid", problem?.Title);
        await AssertAuditMatchesResponse(database, correlationId, response, ct);
    }

    [Fact]
    public async Task Handled_concurrency_exception_is_audited_with_final_409_status()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(
            database.ConnectionString,
            () => new ConcurrencyConflictException("Forced audit contract conflict."));
        using var client = factory.CreateClient();
        Authenticate(client);

        var correlationId = NewCorrelationId("conflict");
        using var request = CreateTestRequest(correlationId);
        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: ct);
        Assert.Equal("concurrency.conflict", problem?.Title);
        await AssertAuditMatchesResponse(database, correlationId, response, ct);
    }

    [Fact]
    public async Task Precondition_failure_is_audited_with_final_412_status()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        Authenticate(client);

        using var createResponse = await client.PostAsJsonAsync(
            "/api/v1/tests",
            new CreateTestRequest("Audit precondition contract"),
            ct);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var testId = await createResponse.Content.ReadFromJsonAsync<TestId>(cancellationToken: ct);

        var correlationId = NewCorrelationId("precondition");
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tests/{testId.Value}/title")
        {
            Content = JsonContent.Create(new RenameTestRequest("Rejected stale write"))
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"999999\"");
        request.Headers.Add(CorrelationAuditMiddleware.CorrelationHeader, correlationId);

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: ct);
        Assert.Equal("concurrency.precondition_failed", problem?.Title);
        await AssertAuditMatchesResponse(database, correlationId, response, ct);
    }

    [Fact]
    public async Task Unhandled_exception_is_audited_with_final_500_status()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(
            database.ConnectionString,
            () => new InvalidOperationException("Forced audit contract failure."));
        using var client = factory.CreateClient();
        Authenticate(client);

        var correlationId = NewCorrelationId("unhandled");
        using var request = CreateTestRequest(correlationId);
        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: ct);
        Assert.Equal("internal.error", problem?.Title);
        await AssertAuditMatchesResponse(database, correlationId, response, ct);
    }

    private static HttpRequestMessage CreateTestRequest(string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tests")
        {
            Content = JsonContent.Create(new CreateTestRequest("Audit status contract"))
        };
        request.Headers.Add(CorrelationAuditMiddleware.CorrelationHeader, correlationId);
        return request;
    }

    private static async Task AssertAuditMatchesResponse(
        PostgreSqlTestDatabase database,
        string correlationId,
        HttpResponseMessage response,
        CancellationToken ct)
    {
        Assert.True(response.Headers.TryGetValues(CorrelationAuditMiddleware.CorrelationHeader, out var values));
        Assert.Equal(correlationId, Assert.Single(values));

        await using var db = database.CreateContext();
        var entry = await db.Set<AuditEntry>()
            .AsNoTracking()
            .SingleAsync(x => x.CorrelationId == correlationId, ct);

        Assert.Equal((int)response.StatusCode, entry.StatusCode);
        Assert.Equal("audit-status-author", entry.ActorId);
        Assert.Equal(correlationId, entry.CorrelationId);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        Func<Exception>? saveFailureFactory = null) =>
        ApiTestHost.Create(
            connectionString,
            configureServices: services =>
            {
                if (saveFailureFactory is not null)
                {
                    services.RemoveAll<IUnitOfWork>();
                    services.AddScoped<IUnitOfWork>(_ => new FailingUnitOfWork(saveFailureFactory));
                }
            });

    private static void Authenticate(HttpClient client)
        => ApiTestHost.Authenticate(client, "audit-status-author", "test-author");

    private static string NewCorrelationId(string outcome) =>
        $"audit-{outcome}-{Guid.CreateVersion7():N}";

    private sealed class FailingUnitOfWork(Func<Exception> failureFactory) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromException(failureFactory());
    }
}
