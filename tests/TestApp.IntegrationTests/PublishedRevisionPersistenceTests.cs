using Microsoft.EntityFrameworkCore;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class PublishedRevisionPersistenceTests
{
    [Fact]
    public async Task Published_revision_round_trips_through_MariaDB()
    {
        await using var database = await MariaDbTestDatabase.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var test = Test.Create("DDD fundamentals", ExternalUserId.FromSubject("author-1"));
        var questionId = test.AddQuestion("What is an aggregate?", QuestionType.SingleChoice, 1m, 1)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        test.AddAnswerOption(questionId, "Consistency boundary", true, 1);
        test.AddAnswerOption(questionId, "Database table", false, 2);
        test.Publish(now);
        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, now);

        await using (var write = database.CreateContext())
        {
            await write.Database.MigrateAsync(ct);
            write.Revisions.Add(revision);
            await write.SaveChangesAsync(ct);
        }

        await using var read = database.CreateContext();
        var persisted = await read.Revisions.AsNoTracking().SingleAsync(x => x.Id == revision.Id, ct);

        Assert.Equal(revision.TestId, persisted.TestId);
        Assert.Equal(1, persisted.Version);
        Assert.Single(persisted.Questions);
        Assert.Equal(2, persisted.Questions.Single().Options.Count);
        Assert.True(persisted.Questions.Single().Options.Single(x => x.Text == "Consistency boundary").IsCorrect);
    }
}
