using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.Domain.Tests.Unit;

/// <summary>
/// Lifecycle rules of <see cref="TestAttempt"/>: answering, clearing, submission,
/// timeout and the deadline boundary between them.
/// </summary>
public sealed class AttemptLifecycleTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-08-16T10:00:00Z");
    private static readonly ExternalUserId Student = ExternalUserId.FromSubject("student-1");
    private static readonly QuestionId QuestionA = QuestionId.New();
    private static readonly QuestionId QuestionB = QuestionId.New();

    private static TestAttempt NewAttempt(DateTimeOffset? deadline = null) =>
        TestAttempt.Start(
            TestAttemptId.New(),
            TestAssignmentId.New(),
            PublishedTestRevisionId.New(),
            Student,
            Guid.NewGuid(),
            Start,
            deadline,
            [QuestionA, QuestionB]);

    private static AttemptScore AnyScore => new(1m, 2m);

    [Fact]
    public void Started_attempt_pre_creates_one_response_slot_per_question()
    {
        var attempt = NewAttempt();

        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
        Assert.Equal(2, attempt.Responses.Count);
        Assert.All(attempt.Responses, response => Assert.Null(response.AnsweredAt));
        Assert.All(attempt.Responses, response => Assert.Empty(response.SelectedOptions));
    }

    [Fact]
    public void Start_deduplicates_repeated_question_ids()
    {
        var attempt = TestAttempt.Start(
            TestAttemptId.New(),
            TestAssignmentId.New(),
            PublishedTestRevisionId.New(),
            Student,
            Guid.NewGuid(),
            Start,
            null,
            [QuestionA, QuestionA, QuestionB]);

        Assert.Equal(2, attempt.Responses.Count);
    }

    [Fact]
    public void Start_rejects_a_deadline_that_is_not_after_the_start_time()
    {
        var exception = Assert.Throws<ArgumentException>(() => TestAttempt.Start(
            TestAttemptId.New(),
            TestAssignmentId.New(),
            PublishedTestRevisionId.New(),
            Student,
            Guid.NewGuid(),
            Start,
            Start,
            [QuestionA]));

        Assert.Equal("deadlineAt", exception.ParamName);
    }

    [Fact]
    public void Start_rejects_an_empty_start_request_id()
    {
        var exception = Assert.Throws<ArgumentException>(() => TestAttempt.Start(
            TestAttemptId.New(),
            TestAssignmentId.New(),
            PublishedTestRevisionId.New(),
            Student,
            Guid.Empty,
            Start,
            null,
            [QuestionA]));

        Assert.Equal("startRequestId", exception.ParamName);
    }

    // ------------------------------------------------------------------ answers

    [Fact]
    public void Answer_records_the_selection_and_the_answer_time()
    {
        var attempt = NewAttempt();
        var option = AnswerOptionId.New();
        var answeredAt = Start.AddMinutes(1);

        var result = attempt.Answer(QuestionA, [option], answeredAt);

        Assert.False(result.TryGetError(out _));
        var response = Assert.Single(attempt.Responses, r => r.Id == QuestionA);
        Assert.Equal(answeredAt, response.AnsweredAt);
        Assert.Equal(option, Assert.Single(response.SelectedOptions).OptionId);
    }

    [Fact]
    public void Answer_replaces_a_previous_selection_rather_than_appending()
    {
        var attempt = NewAttempt();
        var first = AnswerOptionId.New();
        var second = AnswerOptionId.New();

        Assert.False(attempt.Answer(QuestionA, [first], Start.AddMinutes(1)).TryGetError(out _));
        Assert.False(attempt.Answer(QuestionA, [second], Start.AddMinutes(2)).TryGetError(out _));

        var response = Assert.Single(attempt.Responses, r => r.Id == QuestionA);
        Assert.Equal(second, Assert.Single(response.SelectedOptions).OptionId);
    }

    [Fact]
    public void Answer_deduplicates_repeated_option_ids()
    {
        var attempt = NewAttempt();
        var option = AnswerOptionId.New();

        Assert.False(attempt.Answer(QuestionA, [option, option], Start.AddMinutes(1)).TryGetError(out _));

        var response = Assert.Single(attempt.Responses, r => r.Id == QuestionA);
        Assert.Single(response.SelectedOptions);
    }

    [Fact]
    public void Answer_rejects_an_empty_selection()
    {
        var attempt = NewAttempt();

        var result = attempt.Answer(QuestionA, [], Start.AddMinutes(1));

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("attempt.answer.empty", error.Code);
    }

    [Fact]
    public void Answer_rejects_a_question_outside_this_attempt()
    {
        var attempt = NewAttempt();

        var result = attempt.Answer(QuestionId.New(), [AnswerOptionId.New()], Start.AddMinutes(1));

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("attempt.question.not_found", error.Code);
    }

    [Fact]
    public void ClearAnswer_removes_the_selection_and_the_answer_time()
    {
        var attempt = NewAttempt();
        Assert.False(attempt.Answer(QuestionA, [AnswerOptionId.New()], Start.AddMinutes(1)).TryGetError(out _));

        var result = attempt.ClearAnswer(QuestionA, Start.AddMinutes(2));

        Assert.False(result.TryGetError(out _));
        var response = Assert.Single(attempt.Responses, r => r.Id == QuestionA);
        Assert.Null(response.AnsweredAt);
        Assert.Empty(response.SelectedOptions);
    }

    [Fact]
    public void ClearAnswer_rejects_a_question_outside_this_attempt()
    {
        var attempt = NewAttempt();

        var result = attempt.ClearAnswer(QuestionId.New(), Start.AddMinutes(1));

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("attempt.question.not_found", error.Code);
    }

    // ----------------------------------------------------------------- deadline

    [Fact]
    public void IsExpiredAt_is_inclusive_of_the_deadline_instant()
    {
        var deadline = Start.AddMinutes(30);
        var attempt = NewAttempt(deadline);

        Assert.False(attempt.IsExpiredAt(deadline.AddTicks(-1)));
        Assert.True(attempt.IsExpiredAt(deadline));
        Assert.True(attempt.IsExpiredAt(deadline.AddTicks(1)));
    }

    [Fact]
    public void An_attempt_without_a_deadline_never_expires()
    {
        var attempt = NewAttempt();

        Assert.False(attempt.IsExpiredAt(Start.AddYears(10)));
    }

    [Fact]
    public void Answer_after_the_deadline_is_a_conflict()
    {
        var deadline = Start.AddMinutes(30);
        var attempt = NewAttempt(deadline);

        var result = attempt.Answer(QuestionA, [AnswerOptionId.New()], deadline);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("attempt.expired", error.Code);
    }

    [Fact]
    public void ClearAnswer_after_the_deadline_is_a_conflict()
    {
        var deadline = Start.AddMinutes(30);
        var attempt = NewAttempt(deadline);

        var result = attempt.ClearAnswer(QuestionA, deadline);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("attempt.expired", error.Code);
    }

    // --------------------------------------------------------------- completion

    [Fact]
    public void Submit_completes_the_attempt_and_records_the_outcome()
    {
        var attempt = NewAttempt();
        var submittedAt = Start.AddMinutes(5);
        var score = new AttemptScore(3m, 4m);

        var result = attempt.Submit(submittedAt, score, passed: true);

        Assert.False(result.TryGetError(out _));
        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(AttemptOutcome.Passed, attempt.Outcome);
        Assert.Equal(submittedAt, attempt.CompletedAt);
        Assert.Equal(score, attempt.Score);
    }

    [Fact]
    public void Submit_records_a_failing_outcome_when_the_score_is_below_the_threshold()
    {
        var attempt = NewAttempt();

        Assert.False(attempt.Submit(Start.AddMinutes(5), new AttemptScore(1m, 4m), passed: false).TryGetError(out _));

        Assert.Equal(AttemptOutcome.Failed, attempt.Outcome);
    }

    [Fact]
    public void Submit_after_the_deadline_is_a_conflict_and_leaves_the_attempt_in_progress()
    {
        var deadline = Start.AddMinutes(30);
        var attempt = NewAttempt(deadline);

        var result = attempt.Submit(deadline, AnyScore, passed: true);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("attempt.expired", error.Code);
        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
    }

    [Fact]
    public void Submit_rejects_a_completion_time_before_the_start_time()
    {
        var attempt = NewAttempt();

        var result = attempt.Submit(Start.AddMinutes(-1), AnyScore, passed: true);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("attempt.completed_at", error.Code);
    }

    [Fact]
    public void Timeout_completes_the_attempt_as_timed_out()
    {
        var deadline = Start.AddMinutes(30);
        var attempt = NewAttempt(deadline);
        var score = new AttemptScore(1m, 4m);

        var result = attempt.Timeout(deadline.AddMinutes(1), score, passed: false);

        Assert.False(result.TryGetError(out _));
        Assert.Equal(AttemptStatus.TimedOut, attempt.Status);
        Assert.Equal(AttemptOutcome.Failed, attempt.Outcome);
        Assert.Equal(score, attempt.Score);
    }

    [Fact]
    public void Timeout_can_still_record_a_passing_outcome()
    {
        var deadline = Start.AddMinutes(30);
        var attempt = NewAttempt(deadline);

        Assert.False(attempt.Timeout(deadline, new AttemptScore(4m, 4m), passed: true).TryGetError(out _));

        Assert.Equal(AttemptStatus.TimedOut, attempt.Status);
        Assert.Equal(AttemptOutcome.Passed, attempt.Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_completed_attempt_cannot_be_completed_again(bool submitFirst)
    {
        var attempt = NewAttempt();
        var first = submitFirst
            ? attempt.Submit(Start.AddMinutes(5), AnyScore, passed: true)
            : attempt.Timeout(Start.AddMinutes(5), AnyScore, passed: false);
        Assert.False(first.TryGetError(out _));

        var submitAgain = attempt.Submit(Start.AddMinutes(6), AnyScore, passed: true);
        var timeoutAgain = attempt.Timeout(Start.AddMinutes(6), AnyScore, passed: false);

        Assert.True(submitAgain.TryGetError(out var submitError));
        Assert.Equal("attempt.completed", submitError.Code);
        Assert.True(timeoutAgain.TryGetError(out var timeoutError));
        Assert.Equal("attempt.completed", timeoutError.Code);
    }

    [Fact]
    public void A_completed_attempt_cannot_be_answered_or_cleared()
    {
        var attempt = NewAttempt();
        Assert.False(attempt.Submit(Start.AddMinutes(5), AnyScore, passed: true).TryGetError(out _));

        var answer = attempt.Answer(QuestionA, [AnswerOptionId.New()], Start.AddMinutes(6));
        var clear = attempt.ClearAnswer(QuestionA, Start.AddMinutes(6));

        Assert.True(answer.TryGetError(out var answerError));
        Assert.Equal("attempt.completed", answerError.Code);
        Assert.True(clear.TryGetError(out var clearError));
        Assert.Equal("attempt.completed", clearError.Code);
    }

    // -------------------------------------------------------------------- score

    [Theory]
    [InlineData(0, 4, 0)]
    [InlineData(1, 4, 25)]
    [InlineData(4, 4, 100)]
    [InlineData(1, 3, 33.33)]
    public void Percentage_is_rounded_to_two_decimals(decimal earned, decimal maximum, decimal expected)
    {
        Assert.Equal(expected, new AttemptScore(earned, maximum).Percentage);
    }

    [Fact]
    public void Percentage_of_a_zero_point_test_is_zero_rather_than_a_division_error()
    {
        Assert.Equal(0m, new AttemptScore(0m, 0m).Percentage);
    }
}
