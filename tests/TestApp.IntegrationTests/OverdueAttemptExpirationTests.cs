using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TestApp.Application.Abstractions;
using TestApp.Application.Attempts;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Infrastructure.Attempts;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class OverdueAttemptExpirationTests
{
    [Fact]
    public async Task Processor_transitions_overdue_attempt_to_timed_out()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var now = DateTimeOffset.Parse("2026-08-12T14:00:00Z");

        var test = Test.Create("Expiration test", ExternalUserId.FromSubject("author-1"));
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
            1);
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
        var processor = new OverdueAttemptProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IClock>(),
            Options.Create(new AttemptExpirationOptions
            {
                BatchSize = 10,
                PollInterval = TimeSpan.FromMinutes(1)
            }),
            NullLogger<OverdueAttemptProcessor>.Instance);

        var expiredCount = await processor.ProcessBatchAsync(ct);

        Assert.Equal(1, expiredCount);

        await using var verification = database.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(x => x.Id == attempt.Id, ct);
        Assert.Equal(AttemptStatus.TimedOut, persisted.Status);
        Assert.Equal(now, persisted.CompletedAt);
        Assert.NotNull(persisted.Score);
        Assert.Equal(AttemptOutcome.Failed, persisted.Outcome);
        Assert.True(persisted.ConcurrencyVersion > 0);
        Assert.Empty(await verification.OutboxMessages.AsNoTracking().ToArrayAsync(ct));
    }

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
}
