using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Entities;
using TestApp.Domain.Events;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Infrastructure.Outbox;

namespace TestApp.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Test> Tests => Set<Test>();
    public DbSet<PublishedTestRevision> Revisions => Set<PublishedTestRevision>();
    public DbSet<TestAssignment> Assignments => Set<TestAssignment>();
    public DbSet<TestAttempt> Attempts => Set<TestAttempt>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<OutboxDeadLetterAction> OutboxDeadLetterActions => Set<OutboxDeadLetterAction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(512).IsRequired();
            b.Property(x => x.Payload).IsRequired();
            b.HasIndex(x => new { x.ProcessedAt, x.DeadLetteredAt, x.DiscardedAt, x.NextAttemptAt, x.OccurredAt });
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var roots = ChangeTracker.Entries()
            .Where(e => e.Entity is IAggregateRoot)
            .Select(e => (IAggregateRoot)e.Entity)
            .Distinct()
            .ToArray();

        foreach (var integrationEvent in roots.SelectMany(x => x.DomainEvents).OfType<IIntegrationEvent>())
            OutboxMessages.Add(OutboxMessage.From(integrationEvent));

        try
        {
            var affected = await base.SaveChangesAsync(ct);
            foreach (var root in roots)
                root.ClearDomainEvents();
            return affected;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(
                "The resource was changed by another request. Reload the current state and retry the operation.",
                ex);
        }
    }

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken ct)
    {
        await SaveChangesAsync(ct);
    }
}

public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset? DeadLetteredAt { get; private set; }
    public DateTimeOffset? DiscardedAt { get; private set; }
    public string? Error { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage From(IIntegrationEvent e) => new()
    {
        Id = e.EventId,
        OccurredAt = e.OccurredAt.ToUniversalTime(),
        Type = e.GetType().AssemblyQualifiedName ?? e.GetType().FullName!,
        Payload = JsonSerializer.Serialize(e, e.GetType())
    };

    public bool IsDueAt(DateTimeOffset now) =>
        ProcessedAt is null &&
        DeadLetteredAt is null &&
        DiscardedAt is null &&
        (NextAttemptAt is null || NextAttemptAt <= now);

    public void MarkProcessed(DateTimeOffset at)
    {
        if (DiscardedAt is not null)
            throw new InvalidOperationException("Discarded Outbox messages cannot be marked as processed.");

        ProcessedAt = at.ToUniversalTime();
        LastAttemptAt = ProcessedAt;
        NextAttemptAt = null;
        Error = null;
    }

    public void MarkFailed(DateTimeOffset at, string error, DateTimeOffset? nextAttemptAt, int maxAttempts)
    {
        if (DiscardedAt is not null)
            throw new InvalidOperationException("Discarded Outbox messages cannot be retried.");

        at = at.ToUniversalTime();
        AttemptCount++;
        LastAttemptAt = at;
        Error = string.IsNullOrWhiteSpace(error) ? "Unknown outbox publishing error." : error;

        if (AttemptCount >= maxAttempts)
        {
            DeadLetteredAt = at;
            NextAttemptAt = null;
            return;
        }

        NextAttemptAt = nextAttemptAt?.ToUniversalTime();
    }

    public void RequeueDeadLetter(DateTimeOffset at)
    {
        EnsureManageableDeadLetter();
        AttemptCount = 0;
        DeadLetteredAt = null;
        NextAttemptAt = at.ToUniversalTime();
        Error = null;
    }

    public void DiscardDeadLetter(DateTimeOffset at)
    {
        EnsureManageableDeadLetter();
        DiscardedAt = at.ToUniversalTime();
        NextAttemptAt = null;
    }

    private void EnsureManageableDeadLetter()
    {
        if (ProcessedAt is not null)
            throw new InvalidOperationException("Processed Outbox messages are not dead letters.");
        if (DeadLetteredAt is null)
            throw new InvalidOperationException("Only dead-lettered Outbox messages can be managed.");
        if (DiscardedAt is not null)
            throw new InvalidOperationException("Discarded Outbox messages cannot be managed again.");
    }
}
