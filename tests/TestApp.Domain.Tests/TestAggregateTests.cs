using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.Domain.Tests.Unit;

public sealed class TestAggregateTests
{
    private static readonly ExternalUserId Owner = ExternalUserId.FromSubject("author-1");

    [Fact]
    public void Created_test_has_immutable_owner()
    {
        var test = Test.Create("DDD", Owner);
        Assert.Equal(Owner, test.OwnerId);
        Assert.True(test.IsOwnedBy(Owner));
        Assert.False(test.IsOwnedBy(ExternalUserId.FromSubject("author-2")));
    }

    [Fact]
    public void Create_rejects_title_longer_than_persistence_limit()
    {
        Assert.Throws<ArgumentException>(() =>
            Test.Create(new string('x', TestLimits.TitleMaxLength + 1), Owner));
    }

    [Fact]
    public void External_identity_rejects_identifier_longer_than_persistence_limit()
    {
        Assert.Throws<ArgumentException>(() =>
            ExternalUserId.FromSubject(new string('u', ExternalIdentityLimits.MaxIdentifierLength + 1)));
        Assert.Throws<ArgumentException>(() =>
            ExternalGroupId.FromExternalId(new string('g', ExternalIdentityLimits.MaxIdentifierLength + 1)));
    }

    [Fact]
    public void Question_rejects_unknown_type()
    {
        var test = Test.Create("DDD", Owner);

        var result = test.AddQuestion("Unknown type", (QuestionType)999, 1m, 1);

        Assert.False(result.Match(_ => true, _ => false));
        Assert.Empty(test.Questions);
    }

    [Fact]
    public void Publish_requires_questions()
    {
        var test = Test.Create("DDD", Owner);
        var result = test.Publish(DateTimeOffset.UtcNow);
        Assert.False(result.Match(_ => true, _ => false));
        Assert.Equal(TestStatus.Draft, test.Status);
    }

    [Fact]
    public void Editing_published_test_creates_new_draft_definition()
    {
        var test = CreatePublishableTest();
        var published = test.Publish(DateTimeOffset.UtcNow);
        Assert.True(published.Match(_ => true, _ => false));
        Assert.Equal(TestStatus.Published, test.Status);

        var edited = test.AddQuestion("Another", QuestionType.SingleChoice, 1, 2);

        Assert.True(edited.Match(_ => true, _ => false));
        Assert.Equal(TestStatus.Draft, test.Status);
    }

    [Fact]
    public void Single_choice_requires_exactly_one_correct_answer()
    {
        var test = Test.Create("DDD", Owner);
        var question = test.AddQuestion("Choose", QuestionType.SingleChoice, 1, 1)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        test.AddAnswerOption(question, "A", false, 1);
        test.AddAnswerOption(question, "B", false, 2);
        var result = test.Publish(DateTimeOffset.UtcNow);
        Assert.False(result.Match(_ => true, _ => false));
        Assert.Equal(TestStatus.Draft, test.Status);
    }

    [Fact]
    public void Archived_test_cannot_be_edited()
    {
        var test = Test.Create("DDD", Owner);
        Assert.True(test.Archive().Match(_ => true, _ => false));
        var result = test.Rename("New title");
        Assert.False(result.Match(_ => true, _ => false));
        Assert.Equal(TestStatus.Archived, test.Status);
    }

    [Fact]
    public void Published_revision_snapshots_settings()
    {
        var now = DateTimeOffset.Parse("2026-08-12T10:00:00Z");
        var test = CreatePublishableTest();
        Assert.True(test.ChangeSettings(80m, 45).Match(_ => true, _ => false));
        Assert.True(test.Publish(now).Match(_ => true, _ => false));

        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, now);
        test.ChangeSettings(50m, null);

        Assert.Equal(80m, revision.PassingPercentage);
        Assert.Equal(45, revision.TimeLimitMinutes);
        Assert.Equal(now.AddMinutes(45), revision.CalculateDeadline(now));
    }

    [Fact]
    public void Timed_out_attempt_has_score_and_outcome()
    {
        var now = DateTimeOffset.Parse("2026-08-12T10:00:00Z");
        var test = CreatePublishableTest();
        test.ChangeSettings(70m, 30);
        test.Publish(now);
        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, now);
        var attempt = TestAttempt.Start(
            TestAttemptId.New(),
            TestAssignmentId.New(),
            revision.Id,
            ExternalUserId.FromSubject("user-1"),
            Guid.NewGuid(),
            now,
            revision.CalculateDeadline(now),
            revision.Questions.Select(q => q.Id));

        var score = revision.CalculateScore(attempt.Responses);
        var result = attempt.Timeout(now.AddMinutes(30), score, revision.IsPassed(score));

        Assert.True(result.Match(_ => true, _ => false));
        Assert.Equal(AttemptStatus.TimedOut, attempt.Status);
        Assert.Equal(AttemptOutcome.Failed, attempt.Outcome);
        Assert.NotNull(attempt.Score);
    }

    private static Test CreatePublishableTest()
    {
        var test = Test.Create("DDD", Owner);
        var question = test.AddQuestion("What is an aggregate?", QuestionType.SingleChoice, 1, 1)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        test.AddAnswerOption(question, "Consistency boundary", true, 1);
        test.AddAnswerOption(question, "Database table", false, 2);
        return test;
    }
}
