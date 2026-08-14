using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using TestApp.Api;
using TestApp.Infrastructure.Outbox;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class OutboxDeadLetterApiTests
{
    [Fact]
    public async Task Admin_can_inspect_requeue_and_discard_without_payload_exposure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var requeueId = Guid.NewGuid();
        var discardId = Guid.NewGuid();
        const string secretPayload = "{\"secret\":\"must-not-leak\"}";

        await using (var seed = database.CreateContext())
        {
            await seed.Database.MigrateAsync(ct);
            await InsertDeadLetterAsync(seed, requeueId, secretPayload, ct);
            await InsertDeadLetterAsync(seed, discardId, secretPayload, ct);
        }

        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        Authenticate(client, "author-dead-letter", "test-author");
        using (var forbidden = await client.GetAsync($"/api/v1/operations/outbox/dead-letters/{requeueId}", ct))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        Authenticate(client, "admin-dead-letter", "test-admin");
        using (var detail = await client.GetAsync($"/api/v1/operations/outbox/dead-letters/{requeueId}", ct))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var json = await detail.Content.ReadAsStringAsync(ct);
            Assert.DoesNotContain(secretPayload, json, StringComparison.Ordinal);
            Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(requeueId, document.RootElement.GetProperty("eventId").GetGuid());
            Assert.Equal(10, document.RootElement.GetProperty("attemptCount").GetInt32());
        }

        using (var invalid = await client.PostAsJsonAsync(
                   $"/api/v1/operations/outbox/dead-letters/{requeueId}/requeue",
                   new DeadLetterActionRequest(string.Empty),
                   ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var problem = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync(ct));
            Assert.Equal("request.validation", problem.RootElement.GetProperty("title").GetString());
        }

        client.DefaultRequestHeaders.Remove("X-Correlation-ID");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "dead-letter-requeue-correlation");
        using (var requeue = await client.PostAsJsonAsync(
                   $"/api/v1/operations/outbox/dead-letters/{requeueId}/requeue",
                   new DeadLetterActionRequest("Broker issue was fixed."),
                   ct))
        {
            Assert.Equal(HttpStatusCode.OK, requeue.StatusCode);
            var result = await requeue.Content.ReadFromJsonAsync<OutboxDeadLetterDetail>(cancellationToken: ct);
            Assert.NotNull(result);
            Assert.Null(result.DeadLetteredAt);
            Assert.Null(result.DiscardedAt);
            Assert.Equal(0, result.AttemptCount);
        }

        client.DefaultRequestHeaders.Remove("X-Correlation-ID");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "dead-letter-discard-correlation");
        using (var discard = await client.PostAsJsonAsync(
                   $"/api/v1/operations/outbox/dead-letters/{discardId}/discard",
                   new DeadLetterActionRequest("Obsolete after incident remediation."),
                   ct))
        {
            Assert.Equal(HttpStatusCode.OK, discard.StatusCode);
            var result = await discard.Content.ReadFromJsonAsync<OutboxDeadLetterDetail>(cancellationToken: ct);
            Assert.NotNull(result);
            Assert.NotNull(result.DeadLetteredAt);
            Assert.NotNull(result.DiscardedAt);
        }

        await using var verify = database.CreateContext();
        var actions = await verify.OutboxDeadLetterActions.AsNoTracking().ToArrayAsync(ct);
        Assert.Contains(actions, x =>
            x.EventId == requeueId &&
            x.Action == OutboxDeadLetterActionType.Requeued &&
            x.ActorId == "admin-dead-letter" &&
            x.CorrelationId == "dead-letter-requeue-correlation");
        Assert.Contains(actions, x =>
            x.EventId == discardId &&
            x.Action == OutboxDeadLetterActionType.Discarded &&
            x.ActorId == "admin-dead-letter" &&
            x.CorrelationId == "dead-letter-discard-correlation");
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        ApiTestHost.Create(connectionString);

    private static void Authenticate(HttpClient client, string userId, params string[] roles)
        => ApiTestHost.Authenticate(client, userId, roles);

    private static Task InsertDeadLetterAsync(
        AppDbContext db,
        Guid id,
        string payload,
        CancellationToken ct)
    {
        var occurredAt = DateTimeOffset.UtcNow.AddHours(-2);
        var failedAt = DateTimeOffset.UtcNow.AddHours(-1);
        const string type = "Api.SecretIntegrationEvent";
        const string error = "dead-letter-api-probe";
        const int attempts = 10;
        DateTimeOffset? processedAt = null;
        DateTimeOffset? nextAttemptAt = null;
        DateTimeOffset? discardedAt = null;

        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO outbox_messages
                ("Id", "OccurredAt", "Type", "Payload", "ProcessedAt", "Error", "AttemptCount", "LastAttemptAt", "NextAttemptAt", "DeadLetteredAt", "DiscardedAt")
            VALUES
                ({id}, {occurredAt}, {type}, {payload}, {processedAt}, {error}, {attempts}, {failedAt}, {nextAttemptAt}, {failedAt}, {discardedAt});
            """, ct);
    }
}
