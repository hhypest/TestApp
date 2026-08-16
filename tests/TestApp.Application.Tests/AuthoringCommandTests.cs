using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Application.Tests;
using TestApp.Core.Monads;
using TestApp.Domain.Identity;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.Application.Tests.Unit;

/// <summary>
/// Authoring command handlers: ownership boundary, optimistic-concurrency
/// precondition, domain-error translation and persistence behaviour. These paths
/// carry the author-facing write surface of the API.
/// </summary>
public sealed class AuthoringCommandTests
{
    private static readonly ExternalUserId Author = ExternalUserId.FromSubject("author-1");
    private static readonly ExternalUserId Stranger = ExternalUserId.FromSubject("author-2");

    // ------------------------------------------------------ ownership boundary

    [Fact]
    public async Task A_stranger_cannot_add_a_question_to_someone_elses_test()
    {
        var fixture = Fixture.WithQuestion();

        var result = await new AddQuestionCommandHandler(fixture.Tests, Actor.For(Stranger), fixture.UnitOfWork)
            .Handle(new AddQuestionCommand(fixture.Test.Id, "New", QuestionType.SingleChoice, 1m, 5), default);

        AssertError(result, ErrorType.Forbidden, "test.forbidden");
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_administrator_may_edit_a_test_they_do_not_own()
    {
        var fixture = Fixture.WithQuestion();

        var result = await new UpdateQuestionCommandHandler(fixture.Tests, Actor.Admin(Stranger), fixture.UnitOfWork)
            .Handle(new UpdateQuestionCommand(fixture.Test.Id, fixture.QuestionId, "Edited by admin", QuestionType.SingleChoice, 2m), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_missing_test_reports_not_found_before_any_ownership_decision()
    {
        var fixture = Fixture.Empty();

        var result = await new UpdateQuestionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new UpdateQuestionCommand(TestId.New(), QuestionId.New(), "Text", QuestionType.SingleChoice, 1m), default);

        AssertError(result, ErrorType.NotFound, "test.not_found");
    }

    // ------------------------------------------------- concurrency precondition

    [Fact]
    public async Task A_stale_expected_version_is_rejected_before_the_aggregate_is_touched()
    {
        var fixture = Fixture.WithQuestion();
        var staleVersion = fixture.Test.ConcurrencyVersion + 99;

        var result = await new UpdateQuestionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new UpdateQuestionCommand(fixture.Test.Id, fixture.QuestionId, "Edited", QuestionType.SingleChoice, 1m, staleVersion), default);

        AssertError(result, ErrorType.PreconditionFailed, "concurrency.precondition_failed");
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_matching_expected_version_is_accepted()
    {
        var fixture = Fixture.WithQuestion();

        var result = await new UpdateQuestionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new UpdateQuestionCommand(fixture.Test.Id, fixture.QuestionId, "Edited", QuestionType.SingleChoice, 1m, fixture.Test.ConcurrencyVersion), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    // ------------------------------------------------------- question commands

    [Fact]
    public async Task Reordering_an_unknown_question_reports_not_found_rather_than_a_conflict()
    {
        var fixture = Fixture.WithQuestion();

        var result = await new ReorderQuestionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new ReorderQuestionCommand(fixture.Test.Id, QuestionId.New(), fixture.QuestionOrder), default);

        AssertError(result, ErrorType.NotFound, "test.question.not_found");
    }

    [Fact]
    public async Task Reordering_a_question_onto_an_occupied_slot_is_a_conflict()
    {
        var fixture = Fixture.WithQuestion();
        var second = fixture.Test.AddQuestion("Second", QuestionType.SingleChoice, 1m, fixture.QuestionOrder + 1)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));

        var result = await new ReorderQuestionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new ReorderQuestionCommand(fixture.Test.Id, second, fixture.QuestionOrder), default);

        AssertError(result, ErrorType.Conflict, "test.question.order_duplicate");
    }

    [Fact]
    public async Task Removing_a_question_persists_the_change()
    {
        var fixture = Fixture.WithQuestion();

        var result = await new RemoveQuestionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new RemoveQuestionCommand(fixture.Test.Id, fixture.QuestionId), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Test.Questions);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Switching_a_question_to_single_choice_with_two_correct_options_is_a_conflict()
    {
        var fixture = Fixture.WithQuestion(QuestionType.MultipleChoice);
        fixture.AddOption("A", isCorrect: true, order: 0);
        fixture.AddOption("B", isCorrect: true, order: 1);

        var result = await new UpdateQuestionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new UpdateQuestionCommand(fixture.Test.Id, fixture.QuestionId, "Q", QuestionType.SingleChoice, 1m), default);

        AssertError(result, ErrorType.Conflict, "test.single_choice.multiple_correct");
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    // -------------------------------------------------- answer option commands

    [Fact]
    public async Task Updating_an_answer_option_persists_the_new_text_and_correctness()
    {
        var fixture = Fixture.WithQuestion();
        var option = fixture.AddOption("Original", isCorrect: false, order: 0);

        var result = await new UpdateAnswerOptionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new UpdateAnswerOptionCommand(fixture.Test.Id, fixture.QuestionId, option, "Updated", true), default);

        Assert.True(result.IsSuccess);
        var stored = fixture.Question.Options.Single(o => o.Id == option);
        Assert.Equal("Updated", stored.Text);
        Assert.True(stored.IsCorrect);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Updating_an_unknown_answer_option_reports_not_found()
    {
        var fixture = Fixture.WithQuestion();

        var result = await new UpdateAnswerOptionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new UpdateAnswerOptionCommand(fixture.Test.Id, fixture.QuestionId, AnswerOptionId.New(), "Text", false), default);

        AssertError(result, ErrorType.NotFound, "test.answer_option.not_found");
    }

    [Fact]
    public async Task Removing_an_answer_option_persists_the_change()
    {
        var fixture = Fixture.WithQuestion();
        var option = fixture.AddOption("Removable", isCorrect: false, order: 0);

        var result = await new RemoveAnswerOptionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new RemoveAnswerOptionCommand(fixture.Test.Id, fixture.QuestionId, option), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Question.Options);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Reordering_an_unknown_answer_option_reports_not_found_rather_than_a_conflict()
    {
        var fixture = Fixture.WithQuestion();
        fixture.AddOption("Occupying slot 0", isCorrect: true, order: 0);

        var result = await new ReorderAnswerOptionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new ReorderAnswerOptionCommand(fixture.Test.Id, fixture.QuestionId, AnswerOptionId.New(), 0), default);

        AssertError(result, ErrorType.NotFound, "test.answer_option.not_found");
    }

    [Fact]
    public async Task Reordering_an_answer_option_persists_the_new_order()
    {
        var fixture = Fixture.WithQuestion();
        var option = fixture.AddOption("Movable", isCorrect: true, order: 0);

        var result = await new ReorderAnswerOptionCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new ReorderAnswerOptionCommand(fixture.Test.Id, fixture.QuestionId, option, 4), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, fixture.Question.Options.Single(o => o.Id == option).Order);
    }

    [Fact]
    public async Task A_stranger_cannot_reorder_options_on_someone_elses_test()
    {
        var fixture = Fixture.WithQuestion();
        var option = fixture.AddOption("Theirs", isCorrect: true, order: 0);

        var result = await new ReorderAnswerOptionCommandHandler(fixture.Tests, Actor.For(Stranger), fixture.UnitOfWork)
            .Handle(new ReorderAnswerOptionCommand(fixture.Test.Id, fixture.QuestionId, option, 1), default);

        AssertError(result, ErrorType.Forbidden, "test.forbidden");
    }

    // ------------------------------------------------------- lifecycle commands

    [Fact]
    public async Task Archiving_a_test_persists_the_new_status()
    {
        var fixture = Fixture.WithQuestion();

        var result = await new ArchiveTestCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new ArchiveTestCommand(fixture.Test.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestStatus.Archived, fixture.Test.Status);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Archiving_an_already_archived_test_is_a_conflict()
    {
        var fixture = Fixture.WithQuestion();
        Assert.False(fixture.Test.Archive().TryGetError(out _));

        var result = await new ArchiveTestCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new ArchiveTestCommand(fixture.Test.Id), default);

        AssertError(result, ErrorType.Conflict, "test.archived");
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_stranger_cannot_archive_someone_elses_test()
    {
        var fixture = Fixture.WithQuestion();

        var result = await new ArchiveTestCommandHandler(fixture.Tests, Actor.For(Stranger), fixture.UnitOfWork)
            .Handle(new ArchiveTestCommand(fixture.Test.Id), default);

        AssertError(result, ErrorType.Forbidden, "test.forbidden");
        Assert.Equal(TestStatus.Draft, fixture.Test.Status);
    }

    [Fact]
    public async Task Changing_settings_rejects_an_out_of_range_passing_percentage()
    {
        var fixture = Fixture.WithQuestion();

        var result = await new ChangeTestSettingsCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new ChangeTestSettingsCommand(fixture.Test.Id, 101m, null), default);

        Assert.True(result.IsFailure);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Changing_settings_persists_a_valid_threshold_and_time_limit()
    {
        var fixture = Fixture.WithQuestion();

        var result = await new ChangeTestSettingsCommandHandler(fixture.Tests, Actor.For(Author), fixture.UnitOfWork)
            .Handle(new ChangeTestSettingsCommand(fixture.Test.Id, 80m, 45), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(80m, fixture.Test.Settings.PassingPercentage);
        Assert.Equal(45, fixture.Test.Settings.TimeLimitMinutes);
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertError<T>(Result<T, Error> result, ErrorType expectedType, string expectedCode)
        where T : notnull
    {
        Assert.True(result.TryGetError(out var error));
        Assert.Equal(expectedType, error.Type);
        Assert.Equal(expectedCode, error.Code);
    }

    private sealed class Fixture
    {
        public required Test Test { get; init; }
        public required QuestionId QuestionId { get; init; }
        public required int QuestionOrder { get; init; }
        public required TestRepo Tests { get; init; }
        public required RecordingUnitOfWork UnitOfWork { get; init; }

        public Question Question => Test.Questions.Single(q => q.Id == QuestionId);

        public static Fixture Empty() => new()
        {
            Test = NewTest(),
            QuestionId = QuestionId.New(),
            QuestionOrder = 0,
            Tests = new TestRepo(null),
            UnitOfWork = new RecordingUnitOfWork()
        };

        public static Fixture WithQuestion(QuestionType type = QuestionType.SingleChoice)
        {
            var test = NewTest();
            const int order = 0;
            var questionId = test.AddQuestion("Question", type, 1m, order)
                .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
            return new Fixture
            {
                Test = test,
                QuestionId = questionId,
                QuestionOrder = order,
                Tests = new TestRepo(test),
                UnitOfWork = new RecordingUnitOfWork()
            };
        }

        public AnswerOptionId AddOption(string text, bool isCorrect, int order) =>
            Test.AddAnswerOption(QuestionId, text, isCorrect, order)
                .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));

        private static Test NewTest() =>
            Test.Create("Fixture test", Author)
                .Match(test => test, error => throw new Xunit.Sdk.XunitException(error.Message));
    }

    private sealed class TestRepo(Test? value) : ITestRepository
    {
        public Task<Test?> GetAsync(TestId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(value is not null && value.Id == id ? value : null);

        public Task AddAsync(Test test, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>
    /// Counts commits so tests can assert that a rejected command persisted nothing.
    /// </summary>
    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed record Actor(ExternalUserId UserId, IReadOnlySet<string> Roles) : ICurrentActor
    {
        public IReadOnlySet<ExternalGroupId> Groups => new HashSet<ExternalGroupId>();

        public static Actor For(ExternalUserId userId) => new(userId, new HashSet<string> { "test-author" });

        public static Actor Admin(ExternalUserId userId) => new(userId, new HashSet<string> { "test-admin" });
    }
}
