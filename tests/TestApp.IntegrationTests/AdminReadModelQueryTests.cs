using Microsoft.EntityFrameworkCore;
using TestApp.Application.Queries;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class AdminReadModelQueryTests
{
    [Fact]
    public async Task Admin_assignment_pagination_visits_every_row_exactly_once_when_assignedAt_ties()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var assignedAt = DateTimeOffset.Parse("2026-08-16T10:00:00Z");

        var (test, revision) = CreatePublishedTest("Tie-break assignments");
        var assignmentIds = new List<TestAssignmentId>();
        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            for (var i = 0; i < 7; i++)
            {
                var assignment = TestAssignment.Create(
                    TestAssignmentId.New(),
                    revision.Id,
                    new AssignmentTarget.User(ExternalUserId.FromSubject($"student-{i}")),
                    ExternalUserId.FromSubject("admin-1"),
                    assignedAt,
                    assignedAt,
                    assignedAt.AddHours(1),
                    null).Match(a => a, error => throw new Xunit.Sdk.XunitException(error.Message));
                assignmentIds.Add(assignment.Id);
                setup.Assignments.Add(assignment);
            }
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new AssignmentAdminQueries(db);
        var seen = new List<TestAssignmentId>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await queries.GetAssignmentsAsync(null, null, null, null, null, page, 2, ct);
            Assert.Equal(7, result.TotalCount);
            seen.AddRange(result.Items.Select(x => x.Id));
        }

        Assert.Equal(assignmentIds.Count, seen.Count);
        Assert.Equal(assignmentIds.OrderBy(x => x.Value), seen.OrderBy(x => x.Value));
    }

    [Fact]
    public async Task Admin_assignments_are_scoped_by_test_and_revision_filters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var assignedAt = DateTimeOffset.Parse("2026-08-16T10:00:00Z");

        var (testA, revisionA) = CreatePublishedTest("Filter test A");
        var (testB, revisionB) = CreatePublishedTest("Filter test B");
        var assignmentA = TestAssignment.Create(
            TestAssignmentId.New(), revisionA.Id, new AssignmentTarget.User(ExternalUserId.FromSubject("student-a")),
            ExternalUserId.FromSubject("admin-1"), assignedAt, assignedAt, assignedAt.AddHours(1), null)
            .Match(a => a, error => throw new Xunit.Sdk.XunitException(error.Message));
        var assignmentB1 = TestAssignment.Create(
            TestAssignmentId.New(), revisionB.Id, new AssignmentTarget.User(ExternalUserId.FromSubject("student-b1")),
            ExternalUserId.FromSubject("admin-1"), assignedAt, assignedAt, assignedAt.AddHours(1), null)
            .Match(a => a, error => throw new Xunit.Sdk.XunitException(error.Message));
        var assignmentB2 = TestAssignment.Create(
            TestAssignmentId.New(), revisionB.Id, new AssignmentTarget.User(ExternalUserId.FromSubject("student-b2")),
            ExternalUserId.FromSubject("admin-1"), assignedAt, assignedAt, assignedAt.AddHours(1), null)
            .Match(a => a, error => throw new Xunit.Sdk.XunitException(error.Message));

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.AddRange(testA, testB);
            setup.Revisions.AddRange(revisionA, revisionB);
            setup.Assignments.AddRange(assignmentA, assignmentB1, assignmentB2);
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new AssignmentAdminQueries(db);

        var byTestA = await queries.GetAssignmentsAsync(testA.Id, null, null, null, null, 1, 20, ct);
        Assert.Equal(1, byTestA.TotalCount);
        Assert.Equal(assignmentA.Id, Assert.Single(byTestA.Items).Id);

        var byRevisionB = await queries.GetAssignmentsAsync(null, revisionB.Id, null, null, null, 1, 20, ct);
        Assert.Equal(2, byRevisionB.TotalCount);
        Assert.Equal(
            new[] { assignmentB1.Id, assignmentB2.Id }.OrderBy(x => x.Value),
            byRevisionB.Items.Select(x => x.Id).OrderBy(x => x.Value));
    }

    [Fact]
    public async Task Reviewer_results_pagination_visits_every_row_exactly_once_when_startedAt_ties()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var startedAt = DateTimeOffset.Parse("2026-08-16T10:00:00Z");

        var (test, revision) = CreatePublishedTest("Tie-break attempts");
        var attemptIds = new List<TestAttemptId>();
        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(test);
            setup.Revisions.Add(revision);
            for (var i = 0; i < 7; i++)
            {
                var attempt = TestAttempt.Start(
                    TestAttemptId.New(),
                    TestAssignmentId.New(),
                    revision.Id,
                    ExternalUserId.FromSubject($"student-{i}"),
                    Guid.CreateVersion7(),
                    startedAt,
                    null,
                    revision.Questions.Select(q => q.Id));
                attemptIds.Add(attempt.Id);
                setup.Attempts.Add(attempt);
            }
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);
        var seen = new List<TestAttemptId>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await queries.GetReviewerResultsAsync(null, null, null, null, page, 2, ct);
            Assert.Equal(7, result.TotalCount);
            seen.AddRange(result.Items.Select(x => x.AttemptId));
        }

        Assert.Equal(attemptIds.Count, seen.Count);
        Assert.Equal(attemptIds.OrderBy(x => x.Value), seen.OrderBy(x => x.Value));
    }

    [Fact]
    public async Task Reviewer_results_are_scoped_by_test_filter_across_multiple_tests()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var startedAt = DateTimeOffset.Parse("2026-08-16T10:00:00Z");

        var (testA, revisionA) = CreatePublishedTest("Reviewer filter A");
        var (testB, revisionB) = CreatePublishedTest("Reviewer filter B");
        var attemptA = TestAttempt.Start(
            TestAttemptId.New(), TestAssignmentId.New(), revisionA.Id, ExternalUserId.FromSubject("student-a"),
            Guid.CreateVersion7(), startedAt, null, revisionA.Questions.Select(q => q.Id));
        var attemptB1 = TestAttempt.Start(
            TestAttemptId.New(), TestAssignmentId.New(), revisionB.Id, ExternalUserId.FromSubject("student-b1"),
            Guid.CreateVersion7(), startedAt, null, revisionB.Questions.Select(q => q.Id));
        var attemptB2 = TestAttempt.Start(
            TestAttemptId.New(), TestAssignmentId.New(), revisionB.Id, ExternalUserId.FromSubject("student-b2"),
            Guid.CreateVersion7(), startedAt, null, revisionB.Questions.Select(q => q.Id));

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.AddRange(testA, testB);
            setup.Revisions.AddRange(revisionA, revisionB);
            setup.Attempts.AddRange(attemptA, attemptB1, attemptB2);
            await setup.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);

        var byTestA = await queries.GetReviewerResultsAsync(testA.Id, null, null, null, 1, 20, ct);
        Assert.Equal(1, byTestA.TotalCount);
        Assert.Equal(attemptA.Id, Assert.Single(byTestA.Items).AttemptId);

        var byRevisionB = await queries.GetReviewerResultsAsync(null, revisionB.Id, null, null, 1, 20, ct);
        Assert.Equal(2, byRevisionB.TotalCount);
        Assert.Equal(
            new[] { attemptB1.Id, attemptB2.Id }.OrderBy(x => x.Value),
            byRevisionB.Items.Select(x => x.AttemptId).OrderBy(x => x.Value));
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
        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, DateTimeOffset.UtcNow.AddDays(-1));
        test.ClearDomainEvents();
        return (test, revision);
    }
}
