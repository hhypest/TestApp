using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TestApp.Application.Abstractions;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Entities;
using TestApp.Domain.Events;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Test> Tests => Set<Test>();
    public DbSet<PublishedTestRevision> Revisions => Set<PublishedTestRevision>();
    public DbSet<TestAssignment> Assignments => Set<TestAssignment>();
    public DbSet<TestAttempt> Attempts => Set<TestAttempt>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(512).IsRequired();
            b.Property(x => x.Payload).IsRequired();
            b.HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var roots = ChangeTracker.Entries()
            .Where(e => e.Entity is IAggregateRoot)
            .Select(e => (IAggregateRoot)e.Entity)
            .ToArray();

        foreach (var domainEvent in roots.SelectMany(x => x.DomainEvents))
            OutboxMessages.Add(OutboxMessage.From(domainEvent));

        var affected = await base.SaveChangesAsync(ct);
        foreach (var root in roots)
            root.ClearDomainEvents();

        return affected;
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
    public string? Error { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage From(IDomainEvent e) => new()
    {
        Id = e.EventId,
        OccurredAt = e.OccurredAt,
        Type = e.GetType().AssemblyQualifiedName ?? e.GetType().FullName!,
        Payload = JsonSerializer.Serialize(e, e.GetType())
    };

    public void MarkProcessed(DateTimeOffset at)
    {
        ProcessedAt = at;
        Error = null;
    }

    public void MarkFailed(string error) => Error = error;
}
