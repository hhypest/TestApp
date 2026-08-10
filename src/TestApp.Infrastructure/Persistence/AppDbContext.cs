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

    public async Task Commit(CancellationToken ct)
    {
        var events = ChangeTracker.Entries()
            .Where(e => e.Entity is IAggregateRoot)
            .SelectMany(e => ((IAggregateRoot)e.Entity).DomainEvents)
            .ToArray();

        foreach (var domainEvent in events)
        {
            OutboxMessages.Add(OutboxMessage.From(domainEvent));
        }

        await SaveChangesAsync(ct);

        foreach (var entry in ChangeTracker.Entries().Where(e => e.Entity is IAggregateRoot))
            ((IAggregateRoot)entry.Entity).ClearDomainEvents();
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

    public static OutboxMessage From(IDomainEvent domainEvent) => new()
    {
        Id = domainEvent.EventId,
        OccurredAt = domainEvent.OccurredAt,
        Type = domainEvent.GetType().AssemblyQualifiedName ?? domainEvent.GetType().FullName!,
        Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType())
    };

    public void MarkProcessed(DateTimeOffset at) { ProcessedAt = at; Error = null; }
    public void MarkFailed(string error) => Error = error;
}
