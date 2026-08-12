using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TestApp.Application.Queries;

namespace TestApp.Infrastructure.Persistence;

public sealed class AuditEntry
{
    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? ActorId { get; private set; }
    public string Method { get; private set; } = string.Empty;
    public string Route { get; private set; } = string.Empty;
    public int StatusCode { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string TraceId { get; private set; } = string.Empty;
    public decimal DurationMs { get; private set; }

    private AuditEntry() { }

    public static AuditEntry Create(
        DateTimeOffset occurredAt,
        string? actorId,
        string method,
        string route,
        int statusCode,
        string correlationId,
        string traceId,
        decimal durationMs)
    {
        return new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = occurredAt,
            ActorId = string.IsNullOrWhiteSpace(actorId) ? null : actorId.Trim(),
            Method = method,
            Route = route,
            StatusCode = statusCode,
            CorrelationId = correlationId,
            TraceId = traceId,
            DurationMs = Math.Max(0m, durationMs)
        };
    }
}

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("audit_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.ActorId).HasMaxLength(256);
        b.Property(x => x.Method).HasMaxLength(16).IsRequired();
        b.Property(x => x.Route).HasMaxLength(500).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        b.Property(x => x.TraceId).HasMaxLength(64).IsRequired();
        b.Property(x => x.DurationMs).HasPrecision(18, 2);
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => new { x.ActorId, x.OccurredAt });
        b.HasIndex(x => new { x.StatusCode, x.OccurredAt });
    }
}

public sealed record AuditEntryView(
    Guid Id,
    DateTimeOffset OccurredAt,
    string? ActorId,
    string Method,
    string Route,
    int StatusCode,
    string CorrelationId,
    string TraceId,
    decimal DurationMs);

public sealed class AuditTrail(DbContextOptions<AppDbContext> options, TimeProvider time)
{
    public async Task RecordAsync(
        string? actorId,
        string method,
        string route,
        int statusCode,
        string correlationId,
        string traceId,
        decimal durationMs,
        CancellationToken ct = default)
    {
        await using var db = new AppDbContext(options);
        db.Set<AuditEntry>().Add(AuditEntry.Create(
            time.GetUtcNow(), actorId, method, route, statusCode, correlationId, traceId, durationMs));
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<AuditEntryView>> GetAsync(
        string? actorId,
        int? statusCode,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        await using var db = new AppDbContext(options);
        var query = db.Set<AuditEntry>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(actorId))
        {
            var normalized = actorId.Trim();
            query = query.Where(x => x.ActorId == normalized);
        }
        if (statusCode is { } status)
            query = query.Where(x => x.StatusCode == status);
        if (from is { } fromValue)
            query = query.Where(x => x.OccurredAt >= fromValue);
        if (to is { } toValue)
            query = query.Where(x => x.OccurredAt <= toValue);

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditEntryView(
                x.Id,
                x.OccurredAt,
                x.ActorId,
                x.Method,
                x.Route,
                x.StatusCode,
                x.CorrelationId,
                x.TraceId,
                x.DurationMs))
            .ToArrayAsync(ct);

        return new PagedResult<AuditEntryView>(rows, page, pageSize, totalCount);
    }
}
