using TestApp.Application.Abstractions;
using TestApp.Application.Assignments;
using TestApp.Application.Attempts;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.Application.Tests.Unit;

/// <summary>
/// Assignment administration and the attempt commands that sit outside the
/// start/submit happy path: clearing an answer and the administrator's manual
/// timeout. Both are user-visible mutations with their own authorization and
/// deadline semantics.
/// </summary>
public sealed class AssignmentAndAttemptCommandTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-16T10:00:00Z");
    private static readonly ExternalUserId Admin = ExternalUserId.FromSubject("admin-1");
    private static readonly ExternalUserId Student = ExternalUserId.FromSubject("student-1");
    private static readonly ExternalUserId OtherStudent = ExternalUserId.FromSubject("student-2");

    // --------------------------------------------------- assignment lifecycle

    [Fact]
    public async Task Cancelling_an_assignment_records_the_actor_and_persists()
    {
        var assignment = NewAssignment();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new CancelAssignmentCommandHandler(
                new AssignmentRepo(assignment), new Actor(Admin), new Clock(Now.AddHours(1)), unitOfWork)
            .Handle(new CancelAssignmentCommand(assignment.Id, "  superseded  "), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(AssignmentStatus.Cancelled, assignment.Status);
        Assert.Equal(Admin, assignment.CancelledBy);
        Assert.Equal("superseded", assignment.CancelReason);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Cancelling_an_unknown_assignment_reports_not_found()
    {
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new CancelAssignmentCommandHandler(
                new AssignmentRepo(null), new Actor(Admin), new Clock(Now), unitOfWork)
            .Handle(new CancelAssignmentCommand(TestAssignmentId.New(), null), default);

        AssertError(result, ErrorType.NotFound, "assignment.not_found");
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Cancelling_twice_is_a_conflict_and_persists_nothing_the_second_time()
    {
        var assignment = NewAssignment();
        Assert.False(assignment.Cancel(Admin, Now, null).TryGetError(out _));
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new CancelAssignmentCommandHandler(
                new AssignmentRepo(assignment), new Actor(Admin), new Clock(Now), unitOfWork)
            .Handle(new CancelAssignmentCommand(assignment.Id, null), default);

        AssertError(result, ErrorType.Conflict, "assignment.cancelled");
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Changing_the_window_persists_the_new_bounds()
    {
        var assignment = NewAssignment();
        var unitOfWork = new RecordingUnitOfWork();
        var from = Now.AddDays(1);
        var until = Now.AddDays(3);

        var result = await new ChangeAssignmentWindowCommandHandler(new AssignmentRepo(assignment), unitOfWork)
            .Handle(new ChangeAssignmentWindowCommand(assignment.Id, from, until), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(from, assignment.AvailableFrom);
        Assert.Equal(until, assignment.AvailableUntil);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Changing_the_window_rejects_an_inverted_range()
    {
        var assignment = NewAssignment();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new ChangeAssignmentWindowCommandHandler(new AssignmentRepo(assignment), unitOfWork)
            .Handle(new ChangeAssignmentWindowCommand(assignment.Id, Now.AddDays(3), Now.AddDays(1)), default);

        AssertError(result, ErrorType.Validation, "assignment.window");
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Changing_the_attempt_limit_persists_the_new_value()
    {
        var assignment = NewAssignment(attemptLimit: 1);
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new ChangeAssignmentAttemptLimitCommandHandler(new AssignmentRepo(assignment), unitOfWork)
            .Handle(new ChangeAssignmentAttemptLimitCommand(assignment.Id, 5), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, assignment.AttemptLimit);
    }

    [Fact]
    public async Task Changing_the_attempt_limit_rejects_zero()
    {
        var assignment = NewAssignment(attemptLimit: 2);
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new ChangeAssignmentAttemptLimitCommandHandler(new AssignmentRepo(assignment), unitOfWork)
            .Handle(new ChangeAssignmentAttemptLimitCommand(assignment.Id, 0), default);

        AssertError(result, ErrorType.Validation, "assignment.attempt_limit");
        Assert.Equal(2, assignment.AttemptLimit);
    }

    [Fact]
    public async Task A_cancelled_assignment_rejects_further_administration()
    {
        var assignment = NewAssignment();
        Assert.False(assignment.Cancel(Admin, Now, null).TryGetError(out _));
        var unitOfWork = new RecordingUnitOfWork();

        var window = await new ChangeAssignmentWindowCommandHandler(new AssignmentRepo(assignment), unitOfWork)
            .Handle(new ChangeAssignmentWindowCommand(assignment.Id, Now, Now.AddDays(1)), default);
        var limit = await new ChangeAssignmentAttemptLimitCommandHandler(new AssignmentRepo(assignment), unitOfWork)
            .Handle(new ChangeAssignmentAttemptLimitCommand(assignment.Id, 3), default);

        AssertError(window, ErrorType.Conflict, "assignment.cancelled");
        AssertError(limit, ErrorType.Conflict, "assignment.cancelled");
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    // ------------------------------------------------------------ clear answer

    [Fact]
    public async Task Clearing_an_answer_removes_the_selection_and_persists()
    {
        var (revision, attempt, questionId, optionId) = NewAttempt();
        Assert.False(attempt.Answer(questionId, [optionId], Now).TryGetError(out _));
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new ClearAnswerCommandHandler(
                new AttemptRepo(attempt), new RevisionRepo(revision), new Actor(Student), new Clock(Now.AddMinutes(1)), unitOfWork)
            .Handle(new ClearAnswerCommand(attempt.Id, questionId), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(attempt.Responses.Single(r => r.Id == questionId).SelectedOptions);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Clearing_an_answer_on_another_users_attempt_is_forbidden()
    {
        var (revision, attempt, questionId, _) = NewAttempt();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new ClearAnswerCommandHandler(
                new AttemptRepo(attempt), new RevisionRepo(revision), new Actor(OtherStudent), new Clock(Now), unitOfWork)
            .Handle(new ClearAnswerCommand(attempt.Id, questionId), default);

        AssertError(result, ErrorType.Forbidden, "attempt.forbidden");
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Clearing_an_answer_on_an_unknown_attempt_reports_not_found()
    {
        var (revision, _, questionId, _) = NewAttempt();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new ClearAnswerCommandHandler(
                new AttemptRepo(null), new RevisionRepo(revision), new Actor(Student), new Clock(Now), unitOfWork)
            .Handle(new ClearAnswerCommand(TestAttemptId.New(), questionId), default);

        AssertError(result, ErrorType.NotFound, "attempt.not_found");
    }

    [Fact]
    public async Task Clearing_an_answer_after_the_deadline_times_the_attempt_out_and_reports_a_conflict()
    {
        // The handler must not silently accept a late edit: it closes the attempt
        // as TimedOut, persists that, and still answers 409 to the caller.
        var (revision, attempt, questionId, _) = NewAttempt(deadline: Now.AddMinutes(30));
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new ClearAnswerCommandHandler(
                new AttemptRepo(attempt), new RevisionRepo(revision), new Actor(Student), new Clock(Now.AddHours(1)), unitOfWork)
            .Handle(new ClearAnswerCommand(attempt.Id, questionId), default);

        AssertError(result, ErrorType.Conflict, "attempt.expired");
        Assert.Equal(AttemptStatus.TimedOut, attempt.Status);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Clearing_an_unknown_question_reports_not_found()
    {
        var (revision, attempt, _, _) = NewAttempt();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new ClearAnswerCommandHandler(
                new AttemptRepo(attempt), new RevisionRepo(revision), new Actor(Student), new Clock(Now), unitOfWork)
            .Handle(new ClearAnswerCommand(attempt.Id, QuestionId.New()), default);

        AssertError(result, ErrorType.NotFound, "attempt.question.not_found");
    }

    // --------------------------------------------------------- manual timeout

    [Fact]
    public async Task An_administrator_can_time_out_an_in_progress_attempt()
    {
        var (revision, attempt, questionId, optionId) = NewAttempt();
        Assert.False(attempt.Answer(questionId, [optionId], Now).TryGetError(out _));
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new TimeoutAttemptCommandHandler(
                new AttemptRepo(attempt), new RevisionRepo(revision), new Clock(Now.AddMinutes(5)), unitOfWork)
            .Handle(new TimeoutAttemptCommand(attempt.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.TimedOut, attempt.Status);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_manual_timeout_scores_the_answers_recorded_so_far()
    {
        var (revision, attempt, questionId, optionId) = NewAttempt();
        Assert.False(attempt.Answer(questionId, [optionId], Now).TryGetError(out _));

        var result = await new TimeoutAttemptCommandHandler(
                new AttemptRepo(attempt), new RevisionRepo(revision), new Clock(Now.AddMinutes(5)), new RecordingUnitOfWork())
            .Handle(new TimeoutAttemptCommand(attempt.Id), default);

        var score = result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Message));
        Assert.Equal(1m, score.Earned);
        Assert.Equal(AttemptOutcome.Passed, attempt.Outcome);
    }

    [Fact]
    public async Task Timing_out_an_already_completed_attempt_is_a_conflict()
    {
        var (revision, attempt, _, _) = NewAttempt();
        Assert.False(attempt.Submit(Now.AddMinutes(1), new AttemptScore(0m, 1m), passed: false).TryGetError(out _));
        var unitOfWork = new RecordingUnitOfWork();

        var result = await new TimeoutAttemptCommandHandler(
                new AttemptRepo(attempt), new RevisionRepo(revision), new Clock(Now.AddMinutes(5)), unitOfWork)
            .Handle(new TimeoutAttemptCommand(attempt.Id), default);

        AssertError(result, ErrorType.Conflict, "attempt.completed");
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Timing_out_an_unknown_attempt_reports_not_found()
    {
        var (revision, _, _, _) = NewAttempt();

        var result = await new TimeoutAttemptCommandHandler(
                new AttemptRepo(null), new RevisionRepo(revision), new Clock(Now), new RecordingUnitOfWork())
            .Handle(new TimeoutAttemptCommand(TestAttemptId.New()), default);

        AssertError(result, ErrorType.NotFound, "attempt.not_found");
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertError<T>(Result<T, Error> result, ErrorType expectedType, string expectedCode)
        where T : notnull
    {
        Assert.True(result.TryGetError(out var error));
        Assert.Equal(expectedType, error.Type);
        Assert.Equal(expectedCode, error.Code);
    }

    private static TestAssignment NewAssignment(int? attemptLimit = null) =>
        TestAssignment.Create(
            TestAssignmentId.New(),
            PublishedTestRevisionId.New(),
            new AssignmentTarget.User(Student),
            Admin,
            Now,
            Now,
            Now.AddDays(7),
            attemptLimit).Match(a => a, error => throw new Xunit.Sdk.XunitException(error.Message));

    private static (PublishedTestRevision Revision, TestAttempt Attempt, QuestionId QuestionId, AnswerOptionId OptionId) NewAttempt(
        DateTimeOffset? deadline = null)
    {
        var test = Test.Create("Attempt commands", ExternalUserId.FromSubject("author-1"))
            .Match(t => t, error => throw new Xunit.Sdk.XunitException(error.Message));
        var questionId = test.AddQuestion("Pick one", QuestionType.SingleChoice, 1m, 0)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        var optionId = test.AddAnswerOption(questionId, "Correct", true, 0)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        _ = test.AddAnswerOption(questionId, "Wrong", false, 1);
        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, Now.AddDays(-1));

        var attempt = TestAttempt.Start(
            TestAttemptId.New(),
            TestAssignmentId.New(),
            revision.Id,
            Student,
            Guid.CreateVersion7(),
            Now,
            deadline,
            revision.Questions.Select(q => q.Id));

        return (revision, attempt, questionId, optionId);
    }

    private sealed class AssignmentRepo(TestAssignment? value) : ITestAssignmentRepository
    {
        public Task<TestAssignment?> GetAsync(TestAssignmentId id, CancellationToken ct = default) =>
            Task.FromResult(value is not null && value.Id == id ? value : null);

        public Task AddAsync(TestAssignment assignment, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AttemptRepo(TestAttempt? value) : ITestAttemptRepository
    {
        public Task<TestAttempt?> GetAsync(TestAttemptId id, CancellationToken ct = default) =>
            Task.FromResult(value is not null && value.Id == id ? value : null);

        public Task<TestAttemptId?> FindIdByStartRequestAsync(
            TestAssignmentId assignmentId, ExternalUserId userId, Guid startRequestId, CancellationToken ct = default) =>
            Task.FromResult<TestAttemptId?>(null);

        public Task<IReadOnlyCollection<TestAttemptId>> GetExpiredInProgressIdsAsync(
            DateTimeOffset now, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<TestAttemptId>>([]);

        public Task<int> CountAttemptsAsync(
            TestAssignmentId assignmentId, ExternalUserId userId, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task AddAsync(TestAttempt attempt, CancellationToken ct = default) => Task.CompletedTask;

        public Task<TestAttemptId?> TryAddWithinLimitAsync(
            TestAttempt attempt, int? attemptLimit, CancellationToken ct = default) =>
            Task.FromResult<TestAttemptId?>(attempt.Id);
    }

    private sealed class RevisionRepo(PublishedTestRevision revision) : IPublishedTestRevisionRepository
    {
        public Task<PublishedTestRevision?> GetAsync(PublishedTestRevisionId id, CancellationToken ct = default) =>
            Task.FromResult<PublishedTestRevision?>(revision.Id == id ? revision : null);

        public Task<int> GetNextVersionAsync(TestId testId, CancellationToken ct = default) => Task.FromResult(2);

        public Task AddAsync(PublishedTestRevision value, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed record Actor(ExternalUserId UserId) : ICurrentActor
    {
        public IReadOnlySet<ExternalGroupId> Groups => new HashSet<ExternalGroupId>();
        public IReadOnlySet<string> Roles => new HashSet<string>();
    }

    private sealed record Clock(DateTimeOffset UtcNow) : IClock;
}
