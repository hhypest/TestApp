using Microsoft.EntityFrameworkCore;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

/// <summary>
/// The operational audit trail is the forensic record an operator pages through
/// during an incident, so its pagination must be total-ordered: every entry seen
/// exactly once, even when many requests share an <c>OccurredAt</c> timestamp.
/// </summary>
/// <remarks>
/// Honest scope note, mirroring the STAB-006 read-model work: the tie-break test
/// below does <em>not</em> reliably go red against the un-fixed query. Verified by
/// removing the <c>ThenByDescending(x =&gt; x.Id)</c> tie-breaker and re-running —
/// it still passed three times out of three, because PostgreSQL happens to return
/// a stable order for a seq-scanned table this small. It is a guard that pins the
/// intended contract and would catch an ordering change under a plan that does
/// reorder rows, not a proven reproduction of the original defect.
/// </remarks>
public sealed class AuditTrailPaginationTests
{
    private static readonly DateTimeOffset RecordedAt = DateTimeOffset.Parse("2026-08-16T10:00:00Z");

    [Fact]
    public async Task Pagination_visits_every_entry_exactly_once_when_occurredAt_ties()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using (var setup = database.CreateContext())
            await setup.Database.MigrateAsync(ct);

        // A frozen clock makes every entry share the same OccurredAt, which is
        // what a burst of concurrent requests looks like at timestamp resolution.
        var audit = new AuditTrail(OptionsFor(database), new FrozenTimeProvider(RecordedAt));
        for (var i = 0; i < 7; i++)
        {
            await audit.RecordAsync(
                actorId: "actor-1",
                method: "POST",
                route: $"/api/v1/tests/{i}",
                statusCode: 200,
                correlationId: $"correlation-{i}",
                traceId: $"trace-{i}",
                durationMs: 1m,
                ct);
        }

        var seen = new List<Guid>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await audit.GetAsync(null, null, null, null, page, 2, ct);
            Assert.Equal(7, result.TotalCount);
            seen.AddRange(result.Items.Select(x => x.Id));
        }

        Assert.Equal(7, seen.Count);
        Assert.Equal(7, seen.Distinct().Count());
    }

    [Fact]
    public async Task Page_size_is_clamped_and_page_number_is_floored()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using (var setup = database.CreateContext())
            await setup.Database.MigrateAsync(ct);

        var audit = new AuditTrail(OptionsFor(database), new FrozenTimeProvider(RecordedAt));
        await audit.RecordAsync("actor-1", "GET", "/api/v1/tests", 200, "c", "t", 1m, ct);

        var oversized = await audit.GetAsync(null, null, null, null, 1, 5000, ct);
        var negativePage = await audit.GetAsync(null, null, null, null, -3, 20, ct);

        Assert.Equal(100, oversized.PageSize);
        Assert.Equal(1, negativePage.Page);
    }

    [Fact]
    public async Task Entries_can_be_filtered_by_actor_status_and_time_window()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using (var setup = database.CreateContext())
            await setup.Database.MigrateAsync(ct);

        var clock = new FrozenTimeProvider(RecordedAt);
        var audit = new AuditTrail(OptionsFor(database), clock);
        await audit.RecordAsync("actor-1", "POST", "/api/v1/tests", 200, "c1", "t1", 1m, ct);
        await audit.RecordAsync("actor-2", "POST", "/api/v1/tests", 500, "c2", "t2", 1m, ct);
        clock.Advance(TimeSpan.FromHours(2));
        await audit.RecordAsync("actor-1", "POST", "/api/v1/tests", 409, "c3", "t3", 1m, ct);

        var byActor = await audit.GetAsync("actor-1", null, null, null, 1, 20, ct);
        var byStatus = await audit.GetAsync(null, 500, null, null, 1, 20, ct);
        var beforeTheGap = await audit.GetAsync(null, null, null, RecordedAt.AddHours(1), 1, 20, ct);

        Assert.Equal(2, byActor.TotalCount);
        Assert.Equal(500, Assert.Single(byStatus.Items).StatusCode);
        Assert.Equal(2, beforeTheGap.TotalCount);
    }

    private static DbContextOptions<AppDbContext> OptionsFor(PostgreSqlTestDatabase database) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;

    /// <summary>
    /// A manually advanced <see cref="TimeProvider"/>; the audit trail only reads
    /// <see cref="TimeProvider.GetUtcNow"/>, so freezing that is enough to force
    /// the timestamp ties this suite is about.
    /// </summary>
    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
