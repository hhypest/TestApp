using TestApp.Domain.Identity;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.Domain.Tests.Unit;

/// <summary>
/// Authoring-side invariants of the <see cref="Test"/> aggregate: ordering, option
/// uniqueness, question-type consistency and publication readiness.
/// </summary>
public sealed class TestAuthoringInvariantTests
{
    private static readonly ExternalUserId Owner = ExternalUserId.FromSubject("author-1");

    private static Test NewTest(string title = "Sample") =>
        Test.Create(title, Owner).Match(test => test, error => throw new Xunit.Sdk.XunitException(error.Message));

    private static QuestionId AddQuestion(Test test, QuestionType type = QuestionType.SingleChoice, int order = 0) =>
        test.AddQuestion("Question text", type, 1m, order)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));

    private static AnswerOptionId AddOption(Test test, QuestionId questionId, string text, bool isCorrect, int order) =>
        test.AddAnswerOption(questionId, text, isCorrect, order)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));

    // ---------------------------------------------------------------- creation

    [Fact]
    public void Create_trims_title_and_starts_as_draft()
    {
        var test = NewTest("   Spaced title   ");

        Assert.Equal("Spaced title", test.Title);
        Assert.Equal(TestStatus.Draft, test.Status);
        Assert.Empty(test.Questions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_title(string title)
    {
        var result = Test.Create(title, Owner);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.title", error.Code);
    }

    // ------------------------------------------------------- question ordering

    [Fact]
    public void AddQuestion_rejects_duplicate_order()
    {
        var test = NewTest();
        AddQuestion(test, order: 0);

        var result = test.AddQuestion("Another", QuestionType.SingleChoice, 1m, 0);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.question.order_duplicate", error.Code);
    }

    [Fact]
    public void AddQuestion_rejects_non_positive_points()
    {
        var test = NewTest();

        var result = test.AddQuestion("Q", QuestionType.SingleChoice, 0m, 0);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.question.points", error.Code);
    }

    [Fact]
    public void ReorderQuestion_reports_not_found_for_unknown_question_even_when_target_order_is_taken()
    {
        // Regression: the duplicate-order guard used to run before the existence
        // check, so reordering a non-existent question onto an occupied slot
        // answered 409 order_duplicate instead of 404 not_found.
        var test = NewTest();
        AddQuestion(test, order: 0);

        var result = test.ReorderQuestion(QuestionId.New(), 0);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.question.not_found", error.Code);
    }

    [Fact]
    public void ReorderQuestion_still_rejects_duplicate_order_for_an_existing_question()
    {
        var test = NewTest();
        AddQuestion(test, order: 0);
        var second = AddQuestion(test, order: 1);

        var result = test.ReorderQuestion(second, 0);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.question.order_duplicate", error.Code);
    }

    [Fact]
    public void ReorderQuestion_allows_moving_a_question_onto_its_own_current_order()
    {
        var test = NewTest();
        var question = AddQuestion(test, order: 3);

        var result = test.ReorderQuestion(question, 3);

        Assert.False(result.TryGetError(out _));
    }

    // --------------------------------------------------- answer option ordering

    [Fact]
    public void ReorderAnswerOption_reports_not_found_for_unknown_option_even_when_target_order_is_taken()
    {
        // Same regression as ReorderQuestion, on the answer-option path.
        var test = NewTest();
        var question = AddQuestion(test);
        AddOption(test, question, "A", isCorrect: true, order: 0);

        var result = test.ReorderAnswerOption(question, AnswerOptionId.New(), 0);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.answer_option.not_found", error.Code);
    }

    [Fact]
    public void ReorderAnswerOption_still_rejects_duplicate_order_for_an_existing_option()
    {
        var test = NewTest();
        var question = AddQuestion(test);
        AddOption(test, question, "A", isCorrect: true, order: 0);
        var second = AddOption(test, question, "B", isCorrect: false, order: 1);

        var result = test.ReorderAnswerOption(question, second, 0);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.answer_option.order_duplicate", error.Code);
    }

    // ------------------------------------------------- answer option uniqueness

    [Fact]
    public void AddAnswerOption_rejects_case_insensitive_duplicate_text()
    {
        var test = NewTest();
        var question = AddQuestion(test);
        AddOption(test, question, "Paris", isCorrect: true, order: 0);

        var result = test.AddAnswerOption(question, "  paris  ", isCorrect: false, order: 1);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.answer_option.text_duplicate", error.Code);
    }

    [Fact]
    public void SingleChoice_question_rejects_a_second_correct_option()
    {
        var test = NewTest();
        var question = AddQuestion(test, QuestionType.SingleChoice);
        AddOption(test, question, "A", isCorrect: true, order: 0);

        var result = test.AddAnswerOption(question, "B", isCorrect: true, order: 1);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.single_choice.multiple_correct", error.Code);
    }

    [Fact]
    public void MultipleChoice_question_accepts_several_correct_options()
    {
        var test = NewTest();
        var question = AddQuestion(test, QuestionType.MultipleChoice);
        AddOption(test, question, "A", isCorrect: true, order: 0);

        var result = test.AddAnswerOption(question, "B", isCorrect: true, order: 1);

        Assert.False(result.TryGetError(out _));
    }

    [Fact]
    public void UpdateAnswerOption_allows_moving_the_correct_flag_within_a_single_choice_question()
    {
        var test = NewTest();
        var question = AddQuestion(test, QuestionType.SingleChoice);
        var first = AddOption(test, question, "A", isCorrect: true, order: 0);
        var second = AddOption(test, question, "B", isCorrect: false, order: 1);

        Assert.False(test.UpdateAnswerOption(question, first, "A", isCorrect: false).TryGetError(out _));
        Assert.False(test.UpdateAnswerOption(question, second, "B", isCorrect: true).TryGetError(out _));
    }

    // ------------------------------------------------------ question type change

    [Fact]
    public void Changing_a_multiple_choice_question_to_single_choice_rejects_multiple_correct_options()
    {
        // A SingleChoice question with two correct options is an invalid domain
        // state that AddAnswerOption/UpdateAnswerOption already refuse to create.
        // The type-change path must refuse it too instead of deferring the error
        // to publication time.
        var test = NewTest();
        var question = AddQuestion(test, QuestionType.MultipleChoice);
        AddOption(test, question, "A", isCorrect: true, order: 0);
        AddOption(test, question, "B", isCorrect: true, order: 1);

        var result = test.UpdateQuestion(question, "Question text", QuestionType.SingleChoice, 1m);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.single_choice.multiple_correct", error.Code);
    }

    [Fact]
    public void Changing_a_multiple_choice_question_to_single_choice_is_allowed_with_one_correct_option()
    {
        var test = NewTest();
        var question = AddQuestion(test, QuestionType.MultipleChoice);
        AddOption(test, question, "A", isCorrect: true, order: 0);
        AddOption(test, question, "B", isCorrect: false, order: 1);

        var result = test.UpdateQuestion(question, "Question text", QuestionType.SingleChoice, 1m);

        Assert.False(result.TryGetError(out _));
    }

    [Fact]
    public void Changing_a_single_choice_question_to_multiple_choice_is_always_allowed()
    {
        var test = NewTest();
        var question = AddQuestion(test, QuestionType.SingleChoice);
        AddOption(test, question, "A", isCorrect: true, order: 0);

        var result = test.UpdateQuestion(question, "Question text", QuestionType.MultipleChoice, 1m);

        Assert.False(result.TryGetError(out _));
    }

    // ------------------------------------------------------------- publication

    [Fact]
    public void Publish_rejects_a_test_without_questions()
    {
        var test = NewTest();

        var result = test.Publish(DateTimeOffset.UtcNow);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.publish.questions_required", error.Code);
    }

    [Fact]
    public void Publish_rejects_a_question_with_fewer_than_two_options()
    {
        var test = NewTest();
        var question = AddQuestion(test);
        AddOption(test, question, "Only", isCorrect: true, order: 0);

        var result = test.Publish(DateTimeOffset.UtcNow);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.question.options_required", error.Code);
    }

    [Fact]
    public void Publish_rejects_a_single_choice_question_without_a_correct_option()
    {
        var test = NewTest();
        var question = AddQuestion(test, QuestionType.SingleChoice);
        AddOption(test, question, "A", isCorrect: false, order: 0);
        AddOption(test, question, "B", isCorrect: false, order: 1);

        var result = test.Publish(DateTimeOffset.UtcNow);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.single_choice.correct_count", error.Code);
    }

    [Fact]
    public void Publish_rejects_a_multiple_choice_question_without_a_correct_option()
    {
        var test = NewTest();
        var question = AddQuestion(test, QuestionType.MultipleChoice);
        AddOption(test, question, "A", isCorrect: false, order: 0);
        AddOption(test, question, "B", isCorrect: false, order: 1);

        var result = test.Publish(DateTimeOffset.UtcNow);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.multiple_choice.correct_required", error.Code);
    }

    [Fact]
    public void Publish_succeeds_for_a_well_formed_test()
    {
        var test = PublishableTest();

        var result = test.Publish(DateTimeOffset.UtcNow);

        Assert.False(result.TryGetError(out _));
        Assert.Equal(TestStatus.Published, test.Status);
    }

    [Fact]
    public void Editing_a_published_test_returns_it_to_draft()
    {
        var test = PublishableTest();
        Assert.False(test.Publish(DateTimeOffset.UtcNow).TryGetError(out _));
        Assert.Equal(TestStatus.Published, test.Status);

        Assert.False(test.Rename("Renamed after publication").TryGetError(out _));

        Assert.Equal(TestStatus.Draft, test.Status);
    }

    // ---------------------------------------------------------------- archival

    [Fact]
    public void Archived_test_rejects_further_modification()
    {
        var test = PublishableTest();
        Assert.False(test.Archive().TryGetError(out _));

        var rename = test.Rename("New title");
        var addQuestion = test.AddQuestion("Q", QuestionType.SingleChoice, 1m, 99);
        var publish = test.Publish(DateTimeOffset.UtcNow);

        Assert.True(rename.TryGetError(out var renameError));
        Assert.Equal("test.archived", renameError.Code);
        Assert.True(addQuestion.TryGetError(out var addError));
        Assert.Equal("test.archived", addError.Code);
        Assert.True(publish.TryGetError(out var publishError));
        Assert.Equal("test.archived", publishError.Code);
    }

    [Fact]
    public void Archiving_twice_is_a_conflict()
    {
        var test = NewTest();
        Assert.False(test.Archive().TryGetError(out _));

        var result = test.Archive();

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("test.archived", error.Code);
    }

    private static Test PublishableTest()
    {
        var test = NewTest();
        var question = AddQuestion(test, QuestionType.SingleChoice);
        AddOption(test, question, "Correct", isCorrect: true, order: 0);
        AddOption(test, question, "Wrong", isCorrect: false, order: 1);
        return test;
    }
}
