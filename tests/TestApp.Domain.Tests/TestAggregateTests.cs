using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.Domain.Tests.Unit;

public sealed class TestAggregateTests
{
    [Fact]
    public void Publish_requires_questions()
    {
        var test = Test.Create("DDD");

        var result = test.Publish(DateTimeOffset.UtcNow);

        Assert.False(result.Match(_ => true, _ => false));
        Assert.Equal(TestStatus.Draft, test.Status);
    }

    [Fact]
    public void Editing_published_test_creates_new_draft_definition()
    {
        var test = Test.Create("DDD");
        var question = test.AddQuestion("What is an aggregate?", QuestionType.SingleChoice, 1, 1)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        test.AddAnswerOption(question, "Consistency boundary", true, 1);
        test.AddAnswerOption(question, "Database table", false, 2);
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
        var test = Test.Create("DDD");
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
        var test = Test.Create("DDD");
        Assert.True(test.Archive().Match(_ => true, _ => false));

        var result = test.Rename("New title");

        Assert.False(result.Match(_ => true, _ => false));
        Assert.Equal(TestStatus.Archived, test.Status);
    }
}
