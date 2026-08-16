using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TestApp.Application.Abstractions;
using TestApp.Application.Attempts;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Events;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Infrastructure.Attempts;
using TestApp.Infrastructure.Observability;
using TestApp.Infrastructure.Outbox;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class WorkerCycleResilienceTests
{
    [Fact]
    public async Task Outbox_processor_survives_a_failed_cycle_and_recovers_on_the_next_poll()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var integrationEvent = new TestIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, "payload");

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.OutboxMessages.Add(OutboxMessage.From(integrationEvent));
            await setup.SaveChangesAsync(ct);
        }

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(database.ConnectionString));
        services.AddSingleton<OperationalMetrics>();
        services.AddScoped<IOutboxPublisher>(_ => new RecordingPublisher());
        await using var provider = services.BuildServiceProvider();

        var flakyScopes = new FlakyOnceScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var processor = new OutboxProcessor(
            flakyScopes,
            TimeProvider.System,
            Options.Create(new OutboxDeliveryOptions { PollInterval = TimeSpan.FromMilliseconds(30) }),
            provider.GetRequiredService<OperationalMetrics>(),
            NullLogger<OutboxProcessor>.Instance);

        await processor.StartAsync(ct);
        try
        {
            var processed = await WaitUntilAsync(async () =>
            {
                await using var verification = database.CreateContext();
                return await verification.OutboxMessages.AsNoTracking()
                    .AnyAsync(x => x.Id == integrationEvent.EventId && x.ProcessedAt != null, ct);
            }, ct);

            Assert.True(processed, "the message should have been delivered once the worker recovered on a later cycle");
            Assert.True(flakyScopes.FailedOnce, "the first scope acquisition should have been the simulated failure");
            Assert.False(processor.ExecuteTask!.IsFaulted, "a cycle-level failure must not fault the BackgroundService");
        }
        finally
        {
            await processor.StopAsync(ct);
        }
    }

    [Fact]
    public async Task Overdue_attempt_processor_survives_a_failed_cycle_and_recovers_on_the_next_poll()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var now = DateTimeOffset.Parse("2026-08-12T14:00:00Z");

        var test = Test.Create("Expiration resilience", ExternalUserId.FromSubject("author-1"))
            .Match(t => t, error => throw new Xunit.Sdk.XunitException(error.Message));
        _ = test.ChangeSettings(50m, 1);
        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, now.AddMinutes(-10));
        var userId = ExternalUserId.FromSubject("student-expired");
        var assignment = TestAssignment.Create(
            TestAssignmentId.New(),
            revision.Id,
            new AssignmentTarget.User(userId),
            ExternalUserId.FromSubject("admin-1"),
            now.AddMinutes(-5),
            now.AddMinutes(-5),
            now.AddHours(1),
            1).Match(a => a, error => throw new Xunit.Sdk.XunitException(error.Message));
        var attempt = TestAttempt.Start(
            TestAttemptId.New(),
            assignment.Id,
            revision.Id,
            userId,
            Guid.CreateVersion7(),
            now.AddMinutes(-2),
            now.AddMinutes(-1),
            revision.Questions.Select(x => x.Id));

        test.ClearDomainEvents();
        revision.ClearDomainEvents();
        assignment.ClearDomainEvents();
        attempt.ClearDomainEvents();

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            setup.Assignments.Add(assignment);
            setup.Attempts.Add(attempt);
            await setup.SaveChangesAsync(ct);
        }

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(database.ConnectionString));
        services.AddScoped<ITestAttemptRepository, TestAttemptRepository>();
        services.AddScoped<IPublishedTestRevisionRepository, PublishedTestRevisionRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ExpireAttemptCommandHandler>();
        services.AddSingleton<IClock>(new FixedClock(now));
        await using var provider = services.BuildServiceProvider();

        var flakyScopes = new FlakyOnceScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var processor = new OverdueAttemptProcessor(
            flakyScopes,
            provider.GetRequiredService<IClock>(),
            Options.Create(new AttemptExpirationOptions { PollInterval = TimeSpan.FromMilliseconds(30) }),
            NullLogger<OverdueAttemptProcessor>.Instance);

        await processor.StartAsync(ct);
        try
        {
            var expired = await WaitUntilAsync(async () =>
            {
                await using var verification = database.CreateContext();
                return await verification.Attempts.AsNoTracking()
                    .AnyAsync(x => x.Id == attempt.Id && x.Status == AttemptStatus.TimedOut, ct);
            }, ct);

            Assert.True(expired, "the attempt should have expired once the worker recovered on a later cycle");
            Assert.True(flakyScopes.FailedOnce, "the first scope acquisition should have been the simulated failure");
            Assert.False(processor.ExecuteTask!.IsFaulted, "a cycle-level failure must not fault the BackgroundService");
        }
        finally
        {
            await processor.StopAsync(ct);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(25, ct);
        }
        return false;
    }

    private sealed record TestIntegrationEvent(Guid EventId, DateTimeOffset OccurredAt, string Value) : IIntegrationEvent;

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;

    private sealed class RecordingPublisher : IOutboxPublisher
    {
        public Task Publish(Guid eventId, string eventType, string payload, CancellationToken ct) => Task.CompletedTask;
    }

    // Throws once on the first CreateScope() to simulate a transient DB/connectivity failure, then delegates normally.
    private sealed class FlakyOnceScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _pendingFailure = 1;

        public bool FailedOnce { get; private set; }

        public IServiceScope CreateScope()
        {
            if (Interlocked.Exchange(ref _pendingFailure, 0) == 1)
            {
                FailedOnce = true;
                throw new InvalidOperationException("simulated transient scope failure");
            }

            return inner.CreateScope();
        }
    }
}
