using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.Domain.Tests.Unit;

public sealed class TestAggregateTests
{
    [Fact]
    public void Publish_requires_questions()
    {
        var test = Test.Create("DDD");
        Assert.Throws<InvalidOperationException>(() => test.Publish(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Published_test_is_immutable()
    {
        var test = Test.Create("DDD");
        var question = test.AddQuestion("What is an aggregate?", QuestionType.SingleChoice, 1, 1);
        test.AddAnswerOption(question, "Consistency boundary", true, 1);
        test.AddAnswerOption(question, "Database table", false, 2);
        test.Publish(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            test.AddQuestion("Another", QuestionType.SingleChoice, 1, 2));
    }

    [Fact]
    public void Single_choice_requires_exactly_one_correct_answer()
    {
        var test = Test.Create("DDD");
        var question = test.AddQuestion("Choose", QuestionType.SingleChoice, 1, 1);
        test.AddAnswerOption(question, "A", false, 1);
        test.AddAnswerOption(question, "B", false, 2);

        Assert.Throws<InvalidOperationException>(() => test.Publish(DateTimeOffset.UtcNow));
    }
}
