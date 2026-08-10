using TestApp.Domain.Tests;

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
        test.AddQuestion("What is an aggregate?", QuestionType.SingleChoice, 1);
        test.Publish(DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => test.AddQuestion("Another", QuestionType.SingleChoice, 1));
    }

    [Fact]
    public void Single_choice_requires_exactly_one_correct_answer()
    {
        var test = Test.Create("DDD");
        var question = test.AddQuestion("Choose", QuestionType.SingleChoice, 1);
        test.AddAnswerOption(question, "A", false);
        test.AddAnswerOption(question, "B", false);
        Assert.Throws<InvalidOperationException>(() => test.Publish(DateTimeOffset.UtcNow));
    }
}
