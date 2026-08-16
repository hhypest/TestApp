using Microsoft.EntityFrameworkCore;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

/// <summary>
/// The student-facing read models behind <c>/api/v1/me/assignments</c>,
/// <c>/api/v1/me/attempts</c> and <c>/api/v1/attempts/{id}</c>. These decide what a
/// student may see about their own work, so both the visibility boundary and the
/// student-safe projection are asserted here against a real PostgreSQL database.
/// </summary>
public sealed class StudentReadModelQueryTests
{
    private static readonly DateTimeOffset BaseTime = DateTimeOffset.Parse("2026-08-16T10:00:00Z");
    private static readonly ExternalUserId Student = ExternalUserId.FromSubject("student-1");
    private static readonly ExternalUserId OtherStudent = ExternalUserId.FromSubject("student-2");
    private static readonly ExternalGroupId Students = ExternalGroupId.FromExternalId("students");

    [Fact]
    public async Task My_assignments_include_direct_and_group_targets_but_not_other_students()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Visibility");

        var direct = NewAssignment(revision, new AssignmentTarget.User(Student));
        var viaGroup = NewAssignment(revision, new AssignmentTarget.Group(Students));
        var someoneElse = NewAssignment(revision, new AssignmentTarget.User(OtherStudent));
        var otherGroup = NewAssignment(revision, new AssignmentTarget.Group(ExternalGroupId.FromExternalId("teachers")));

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            setup.Assignments.AddRange(direct, viaGroup, someoneElse, otherGroup);
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);

        var result = await queries.GetAssignmentsAsync(
            Student, new HashSet<ExternalGroupId> { Students }, 1, 20, null, ct);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(
            new[] { direct.Id, viaGroup.Id }.OrderBy(x => x.Value),
            result.Items.Select(x => x.Id).OrderBy(x => x.Value));
    }

    [Fact]
    public async Task My_assignments_drop_group_targets_once_the_group_claim_is_gone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Group claim");
        var viaGroup = NewAssignment(revision, new AssignmentTarget.Group(Students));

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            setup.Assignments.Add(viaGroup);
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);

        var withClaim = await queries.GetAssignmentsAsync(
            Student, new HashSet<ExternalGroupId> { Students }, 1, 20, null, ct);
        var withoutClaim = await queries.GetAssignmentsAsync(
            Student, new HashSet<ExternalGroupId>(), 1, 20, null, ct);

        Assert.Equal(1, withClaim.TotalCount);
        Assert.Equal(0, withoutClaim.TotalCount);
    }

    [Fact]
    public async Task My_assignments_carry_the_revision_snapshot_not_the_live_test()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Snapshot title");
        var assignment = NewAssignment(revision, new AssignmentTarget.User(Student));

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            setup.Assignments.Add(assignment);
            await setup.SaveChangesAsync(ct);
        }

        // Rename the working test after the revision was published.
        await using (var edit = database.CreateContext())
        {
            var live = await edit.Tests.SingleAsync(x => x.Id == test.Id, ct);
            Assert.False(live.Rename("Renamed after publication").TryGetError(out _));
            live.ClearDomainEvents();
            await edit.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);

        var result = await queries.GetAssignmentsAsync(
            Student, new HashSet<ExternalGroupId>(), 1, 20, null, ct);

        var item = Assert.Single(result.Items);
        Assert.Equal("Snapshot title", item.TestTitle);
        Assert.Equal(1, item.RevisionVersion);
    }

    [Fact]
    public async Task My_assignments_can_be_filtered_by_status()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Status filter");
        var active = NewAssignment(revision, new AssignmentTarget.User(Student));
        var cancelled = NewAssignment(revision, new AssignmentTarget.User(Student));
        Assert.False(cancelled.Cancel(ExternalUserId.FromSubject("admin-1"), BaseTime.AddHours(1), "obsolete")
            .TryGetError(out _));
        cancelled.ClearDomainEvents();

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            setup.Assignments.AddRange(active, cancelled);
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);

        var activeOnly = await queries.GetAssignmentsAsync(
            Student, new HashSet<ExternalGroupId>(), 1, 20, AssignmentStatus.Active, ct);
        var cancelledOnly = await queries.GetAssignmentsAsync(
            Student, new HashSet<ExternalGroupId>(), 1, 20, AssignmentStatus.Cancelled, ct);

        Assert.Equal(active.Id, Assert.Single(activeOnly.Items).Id);
        Assert.Equal(cancelled.Id, Assert.Single(cancelledOnly.Items).Id);
    }

    [Fact]
    public async Task My_assignment_pagination_visits_every_row_exactly_once_when_assignedAt_ties()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Tie-break me/assignments");

        var expected = new List<TestAssignmentId>();
        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            for (var i = 0; i < 7; i++)
            {
                var assignment = NewAssignment(revision, new AssignmentTarget.User(Student));
                expected.Add(assignment.Id);
                setup.Assignments.Add(assignment);
            }
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);

        var seen = new List<TestAssignmentId>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await queries.GetAssignmentsAsync(
                Student, new HashSet<ExternalGroupId>(), page, 2, null, ct);
            Assert.Equal(7, result.TotalCount);
            seen.AddRange(result.Items.Select(x => x.Id));
        }

        Assert.Equal(expected.Count, seen.Count);
        Assert.Equal(expected.OrderBy(x => x.Value), seen.OrderBy(x => x.Value));
    }

    [Fact]
    public async Task My_attempts_are_scoped_to_the_current_user()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Attempt scope");
        var mine = NewAttempt(revision, Student);
        var theirs = NewAttempt(revision, OtherStudent);

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            setup.Attempts.AddRange(mine, theirs);
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);

        var result = await queries.GetAttemptsAsync(Student, 1, 20, null, ct);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(mine.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task My_attempt_pagination_visits_every_row_exactly_once_when_startedAt_ties()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Tie-break me/attempts");

        var expected = new List<TestAttemptId>();
        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            for (var i = 0; i < 7; i++)
            {
                var attempt = NewAttempt(revision, Student);
                expected.Add(attempt.Id);
                setup.Attempts.Add(attempt);
            }
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);

        var seen = new List<TestAttemptId>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await queries.GetAttemptsAsync(Student, page, 2, null, ct);
            Assert.Equal(7, result.TotalCount);
            seen.AddRange(result.Items.Select(x => x.Id));
        }

        Assert.Equal(expected.Count, seen.Count);
        Assert.Equal(expected.OrderBy(x => x.Value), seen.OrderBy(x => x.Value));
    }

    [Fact]
    public async Task Attempt_detail_is_returned_for_the_owner_and_withheld_from_everyone_else()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Attempt detail");
        var attempt = NewAttempt(revision, Student);
        var questionId = revision.Questions.First().Id;
        var optionId = revision.Questions.First().Options.First().Id;
        Assert.False(attempt.Answer(questionId, [optionId], BaseTime.AddMinutes(1)).TryGetError(out _));

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            setup.Attempts.Add(attempt);
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);

        var owned = await queries.GetAttemptAsync(attempt.Id, Student, ct);
        var foreign = await queries.GetAttemptAsync(attempt.Id, OtherStudent, ct);

        Assert.Null(foreign);
        Assert.NotNull(owned);
        Assert.Equal(attempt.Id, owned.Id);
        var response = Assert.Single(owned.Responses, r => r.QuestionId == questionId);
        Assert.Equal(optionId, Assert.Single(response.SelectedOptionIds));
        Assert.Equal(BaseTime.AddMinutes(1), response.AnsweredAt);
    }

    [Fact]
    public async Task Attempt_result_is_returned_for_the_owner_and_withheld_from_everyone_else()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Attempt result");
        var attempt = NewAttempt(revision, Student);
        var score = revision.CalculateScore(attempt.Responses);
        Assert.False(attempt.Submit(BaseTime.AddMinutes(5), score, revision.IsPassed(score)).TryGetError(out _));
        attempt.ClearDomainEvents();

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            setup.Attempts.Add(attempt);
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);

        var owned = await queries.GetAttemptResultAsync(attempt.Id, Student, ct);
        var foreign = await queries.GetAttemptResultAsync(attempt.Id, OtherStudent, ct);

        Assert.Null(foreign);
        Assert.NotNull(owned);
        Assert.Equal(AttemptStatus.Submitted, owned.Status);
        Assert.Equal(score.Earned, owned.Earned);
        Assert.Equal(score.Maximum, owned.Maximum);
    }

    private static TestAssignment NewAssignment(PublishedTestRevision revision, AssignmentTarget target)
    {
        var assignment = TestAssignment.Create(
            TestAssignmentId.New(),
            revision.Id,
            target,
            ExternalUserId.FromSubject("admin-1"),
            BaseTime,
            BaseTime,
            BaseTime.AddDays(7),
            null).Match(a => a, error => throw new Xunit.Sdk.XunitException(error.Message));
        assignment.ClearDomainEvents();
        return assignment;
    }

    private static TestAttempt NewAttempt(PublishedTestRevision revision, ExternalUserId userId)
    {
        var attempt = TestAttempt.Start(
            TestAttemptId.New(),
            TestAssignmentId.New(),
            revision.Id,
            userId,
            Guid.CreateVersion7(),
            BaseTime,
            null,
            revision.Questions.Select(q => q.Id));
        attempt.ClearDomainEvents();
        return attempt;
    }

    private static (Test Test, PublishedTestRevision Revision) CreatePublishedTest(string title)
    {
        var test = Test.Create(title, ExternalUserId.FromSubject("author-1"))
            .Match(t => t, error => throw new Xunit.Sdk.XunitException(error.Message));
        _ = test.ChangeSettings(50m, null);
        var questionId = test.AddQuestion("Pick one", QuestionType.SingleChoice, 1m, 1)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        test.AddAnswerOption(questionId, "Correct", true, 1);
        test.AddAnswerOption(questionId, "Wrong", false, 2);
        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, BaseTime.AddDays(-1));
        test.ClearDomainEvents();
        return (test, revision);
    }
}
