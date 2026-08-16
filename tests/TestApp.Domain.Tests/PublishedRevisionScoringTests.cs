using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.Domain.Tests.Unit;

/// <summary>
/// Snapshot, answer-validation and exact-set scoring behaviour of
/// <see cref="PublishedTestRevision"/> — the rules that decide a student's grade.
/// </summary>
public sealed class PublishedRevisionScoringTests
{
    private static readonly ExternalUserId Owner = ExternalUserId.FromSubject("author-1");
    private static readonly ExternalUserId Student = ExternalUserId.FromSubject("student-1");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-16T10:00:00Z");

    /// <summary>
    /// Builds a two-question revision: a 1-point SingleChoice and a 3-point
    /// MultipleChoice with two correct options out of three.
    /// </summary>
    private static (PublishedTestRevision Revision, QuestionId Single, QuestionId Multi) BuildRevision(
        decimal passingPercentage = 50m,
        int? timeLimitMinutes = null)
    {
        var test = Test.Create("Scoring", Owner).Match(t => t, e => throw new Xunit.Sdk.XunitException(e.Message));
        Assert.False(test.ChangeSettings(passingPercentage, timeLimitMinutes).TryGetError(out _));

        var single = test.AddQuestion("Single", QuestionType.SingleChoice, 1m, 0)
            .Match(id => id, e => throw new Xunit.Sdk.XunitException(e.Message));
        Add(test, single, "S-correct", true, 0);
        Add(test, single, "S-wrong", false, 1);

        var multi = test.AddQuestion("Multi", QuestionType.MultipleChoice, 3m, 1)
            .Match(id => id, e => throw new Xunit.Sdk.XunitException(e.Message));
        Add(test, multi, "M-correct-1", true, 0);
        Add(test, multi, "M-correct-2", true, 1);
        Add(test, multi, "M-wrong", false, 2);

        Assert.False(test.Publish(Now).TryGetError(out _));
        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, Now);
        return (revision, single, multi);
    }

    private static void Add(Test test, QuestionId questionId, string text, bool isCorrect, int order) =>
        Assert.False(test.AddAnswerOption(questionId, text, isCorrect, order).TryGetError(out _));

    private static AnswerOptionId OptionId(PublishedTestRevision revision, QuestionId questionId, string text) =>
        revision.Questions.Single(q => q.Id == questionId).Options.Single(o => o.Text == text).Id;

    private static TestAttempt AttemptWith(
        PublishedTestRevision revision,
        params (QuestionId Question, AnswerOptionId[] Options)[] answers)
    {
        var attempt = TestAttempt.Start(
            TestAttemptId.New(),
            TestAssignmentId.New(),
            revision.Id,
            Student,
            Guid.NewGuid(),
            Now,
            null,
            revision.Questions.Select(q => q.Id));

        foreach (var (question, options) in answers)
            Assert.False(attempt.Answer(question, options, Now.AddMinutes(1)).TryGetError(out _));

        return attempt;
    }

    // ------------------------------------------------------------- snapshotting

    [Fact]
    public void From_snapshots_questions_and_options_in_authored_order()
    {
        var (revision, single, multi) = BuildRevision();

        Assert.Equal([single, multi], revision.Questions.Select(q => q.Id));
        Assert.Equal(
            ["M-correct-1", "M-correct-2", "M-wrong"],
            revision.Questions.Single(q => q.Id == multi).Options.Select(o => o.Text));
    }

    [Fact]
    public void From_copies_title_and_settings_from_the_test()
    {
        var (revision, _, _) = BuildRevision(passingPercentage: 65m, timeLimitMinutes: 30);

        Assert.Equal("Scoring", revision.Title);
        Assert.Equal(65m, revision.PassingPercentage);
        Assert.Equal(30, revision.TimeLimitMinutes);
        Assert.Equal(1, revision.Version);
    }

    [Fact]
    public void A_later_edit_of_the_test_does_not_change_an_existing_revision()
    {
        var test = Test.Create("Original", Owner).Match(t => t, e => throw new Xunit.Sdk.XunitException(e.Message));
        var question = test.AddQuestion("Q", QuestionType.SingleChoice, 1m, 0)
            .Match(id => id, e => throw new Xunit.Sdk.XunitException(e.Message));
        Add(test, question, "A", true, 0);
        Add(test, question, "B", false, 1);
        Assert.False(test.Publish(Now).TryGetError(out _));

        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, Now);
        Assert.False(test.Rename("Changed after publication").TryGetError(out _));

        Assert.Equal("Original", revision.Title);
    }

    // ---------------------------------------------------------------- deadlines

    [Fact]
    public void CalculateDeadline_returns_null_when_the_revision_has_no_time_limit()
    {
        var (revision, _, _) = BuildRevision(timeLimitMinutes: null);

        Assert.Null(revision.CalculateDeadline(Now));
    }

    [Fact]
    public void CalculateDeadline_offsets_the_start_time_by_the_time_limit()
    {
        var (revision, _, _) = BuildRevision(timeLimitMinutes: 45);

        Assert.Equal(Now.AddMinutes(45), revision.CalculateDeadline(Now));
    }

    // ------------------------------------------------------- answer validation

    [Fact]
    public void ValidateAnswer_rejects_a_question_outside_the_revision()
    {
        var (revision, _, _) = BuildRevision();

        var result = revision.ValidateAnswer(QuestionId.New(), [AnswerOptionId.New()]);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("revision.question.not_found", error.Code);
    }

    [Fact]
    public void ValidateAnswer_rejects_an_empty_selection()
    {
        var (revision, single, _) = BuildRevision();

        var result = revision.ValidateAnswer(single, []);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("revision.answer.empty", error.Code);
    }

    [Fact]
    public void ValidateAnswer_rejects_multiple_selections_for_a_single_choice_question()
    {
        var (revision, single, _) = BuildRevision();
        var a = OptionId(revision, single, "S-correct");
        var b = OptionId(revision, single, "S-wrong");

        var result = revision.ValidateAnswer(single, [a, b]);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("revision.single_choice.selection_count", error.Code);
    }

    [Fact]
    public void ValidateAnswer_rejects_an_option_belonging_to_another_question()
    {
        var (revision, single, multi) = BuildRevision();
        var foreignOption = OptionId(revision, multi, "M-correct-1");

        var result = revision.ValidateAnswer(single, [foreignOption]);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("revision.answer.invalid_option", error.Code);
    }

    [Fact]
    public void ValidateAnswer_accepts_several_options_for_a_multiple_choice_question()
    {
        var (revision, _, multi) = BuildRevision();
        var a = OptionId(revision, multi, "M-correct-1");
        var b = OptionId(revision, multi, "M-wrong");

        var result = revision.ValidateAnswer(multi, [a, b]);

        Assert.False(result.TryGetError(out _));
    }

    // ------------------------------------------------------------ exact scoring

    [Fact]
    public void An_unanswered_attempt_scores_zero_out_of_the_full_maximum()
    {
        var (revision, _, _) = BuildRevision();
        var attempt = AttemptWith(revision);

        var score = revision.CalculateScore(attempt.Responses);

        Assert.Equal(0m, score.Earned);
        Assert.Equal(4m, score.Maximum);
    }

    [Fact]
    public void A_fully_correct_attempt_earns_every_point()
    {
        var (revision, single, multi) = BuildRevision();
        var attempt = AttemptWith(
            revision,
            (single, [OptionId(revision, single, "S-correct")]),
            (multi, [OptionId(revision, multi, "M-correct-1"), OptionId(revision, multi, "M-correct-2")]));

        var score = revision.CalculateScore(attempt.Responses);

        Assert.Equal(4m, score.Earned);
        Assert.Equal(4m, score.Maximum);
        Assert.Equal(100m, score.Percentage);
    }

    [Fact]
    public void A_partially_correct_multiple_choice_selection_earns_nothing()
    {
        // Exact-set scoring: selecting one of two correct options is not partial credit.
        var (revision, _, multi) = BuildRevision();
        var attempt = AttemptWith(revision, (multi, [OptionId(revision, multi, "M-correct-1")]));

        var score = revision.CalculateScore(attempt.Responses);

        Assert.Equal(0m, score.Earned);
    }

    [Fact]
    public void Selecting_every_option_of_a_multiple_choice_question_earns_nothing()
    {
        var (revision, _, multi) = BuildRevision();
        var attempt = AttemptWith(revision, (multi,
        [
            OptionId(revision, multi, "M-correct-1"),
            OptionId(revision, multi, "M-correct-2"),
            OptionId(revision, multi, "M-wrong")
        ]));

        var score = revision.CalculateScore(attempt.Responses);

        Assert.Equal(0m, score.Earned);
    }

    [Fact]
    public void Question_points_are_weighted_rather_than_counted_equally()
    {
        // Answering only the 3-point MultipleChoice must beat answering only the
        // 1-point SingleChoice.
        var (revision, single, multi) = BuildRevision();

        var multiOnly = revision.CalculateScore(AttemptWith(revision, (multi,
            [OptionId(revision, multi, "M-correct-1"), OptionId(revision, multi, "M-correct-2")])).Responses);
        var singleOnly = revision.CalculateScore(AttemptWith(revision, (single,
            [OptionId(revision, single, "S-correct")])).Responses);

        Assert.Equal(3m, multiOnly.Earned);
        Assert.Equal(1m, singleOnly.Earned);
    }

    // ------------------------------------------------------------ pass/fail line

    [Fact]
    public void IsPassed_treats_the_passing_percentage_as_inclusive()
    {
        var (revision, _, _) = BuildRevision(passingPercentage: 50m);

        Assert.True(revision.IsPassed(new AttemptScore(2m, 4m)));   // exactly 50%
        Assert.False(revision.IsPassed(new AttemptScore(1m, 4m)));  // 25%
        Assert.True(revision.IsPassed(new AttemptScore(3m, 4m)));   // 75%
    }

    [Fact]
    public void A_zero_percent_threshold_passes_an_empty_attempt()
    {
        var (revision, _, _) = BuildRevision(passingPercentage: 0m);
        var attempt = AttemptWith(revision);

        Assert.True(revision.IsPassed(revision.CalculateScore(attempt.Responses)));
    }

    [Fact]
    public void A_hundred_percent_threshold_requires_every_question()
    {
        var (revision, single, multi) = BuildRevision(passingPercentage: 100m);
        var partial = AttemptWith(revision, (single, [OptionId(revision, single, "S-correct")]));
        var full = AttemptWith(
            revision,
            (single, [OptionId(revision, single, "S-correct")]),
            (multi, [OptionId(revision, multi, "M-correct-1"), OptionId(revision, multi, "M-correct-2")]));

        Assert.False(revision.IsPassed(revision.CalculateScore(partial.Responses)));
        Assert.True(revision.IsPassed(revision.CalculateScore(full.Responses)));
    }
}
