using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

/// <summary>
/// Student-safe attempt presentation and resume (ATT-010 / ATT-011 / UX-001).
/// </summary>
/// <remarks>
/// The revision snapshot stores every option's correctness flag in the same
/// <c>jsonb</c> column the presentation is built from, so withholding the answer key is
/// purely a property of the projection code. That makes the serialized-payload test in
/// this file the load-bearing one: it asserts on the bytes a student actually receives,
/// not on the shape of a DTO that a future edit could widen.
/// </remarks>
public sealed class AttemptPresentationTests
{
    private static readonly DateTimeOffset BaseTime = DateTimeOffset.Parse("2026-08-16T10:00:00Z");
    private static readonly ExternalUserId Student = ExternalUserId.FromSubject("student-1");
    private static readonly ExternalUserId OtherStudent = ExternalUserId.FromSubject("student-2");

    // ------------------------------------------------------- presentation content

    [Fact]
    public async Task Presentation_returns_questions_and_options_in_authored_order()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Presentation order");
        var attempt = NewAttempt(revision);
        await SeedAsync(database, ct, test, revision, attempt);

        await using var db = database.CreateContext();
        var view = await new ReadModelQueries(db)
            .GetAttemptPresentationAsync(attempt.Id, Student, BaseTime, ct);

        Assert.NotNull(view);
        Assert.Equal("Presentation order", view.TestTitle);
        Assert.Equal(1, view.RevisionVersion);
        var question = Assert.Single(view.Questions);
        Assert.Equal("Pick one", question.Text);
        Assert.Equal(QuestionType.SingleChoice, question.Type);
        Assert.Equal(["Correct", "Wrong"], question.Options.Select(o => o.Text));
    }

    [Fact]
    public async Task Presentation_merges_the_students_own_saved_answers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Saved answers");
        var attempt = NewAttempt(revision);
        var questionId = revision.Questions.First().Id;
        var optionId = revision.Questions.First().Options.First().Id;
        Assert.False(attempt.Answer(questionId, [optionId], BaseTime.AddMinutes(2)).TryGetError(out _));
        await SeedAsync(database, ct, test, revision, attempt);

        await using var db = database.CreateContext();
        var view = await new ReadModelQueries(db)
            .GetAttemptPresentationAsync(attempt.Id, Student, BaseTime, ct);

        var question = Assert.Single(view!.Questions);
        Assert.Equal(optionId, Assert.Single(question.SelectedOptionIds));
        Assert.Equal(BaseTime.AddMinutes(2), question.AnsweredAt);
    }

    [Fact]
    public async Task An_unanswered_question_presents_an_empty_selection_rather_than_being_omitted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Unanswered");
        var attempt = NewAttempt(revision);
        await SeedAsync(database, ct, test, revision, attempt);

        await using var db = database.CreateContext();
        var view = await new ReadModelQueries(db)
            .GetAttemptPresentationAsync(attempt.Id, Student, BaseTime, ct);

        var question = Assert.Single(view!.Questions);
        Assert.Empty(question.SelectedOptionIds);
        Assert.Null(question.AnsweredAt);
    }

    [Fact]
    public async Task Presentation_comes_from_the_revision_snapshot_not_the_edited_test()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Original title");
        var attempt = NewAttempt(revision);
        await SeedAsync(database, ct, test, revision, attempt);

        await using (var edit = database.CreateContext())
        {
            var live = await edit.Tests.Include(x => x.Questions).SingleAsync(x => x.Id == test.Id, ct);
            Assert.False(live.Rename("Renamed mid-attempt").TryGetError(out _));
            live.ClearDomainEvents();
            await edit.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext();
        var view = await new ReadModelQueries(db)
            .GetAttemptPresentationAsync(attempt.Id, Student, BaseTime, ct);

        Assert.Equal("Original title", view!.TestTitle);
    }

    [Fact]
    public async Task Presentation_carries_the_timing_needed_for_a_countdown()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Timing", timeLimitMinutes: 30);
        var attempt = NewAttempt(revision, deadline: BaseTime.AddMinutes(30));
        await SeedAsync(database, ct, test, revision, attempt);
        var serverTime = BaseTime.AddMinutes(7);

        await using var db = database.CreateContext();
        var view = await new ReadModelQueries(db)
            .GetAttemptPresentationAsync(attempt.Id, Student, serverTime, ct);

        Assert.Equal(BaseTime, view!.StartedAt);
        Assert.Equal(BaseTime.AddMinutes(30), view.DeadlineAt);
        Assert.Equal(serverTime, view.ServerTime);
        Assert.Equal(30, view.TimeLimitMinutes);
    }

    [Fact]
    public async Task A_past_deadline_is_reported_without_the_read_side_completing_the_attempt()
    {
        // Between the deadline and the expiration worker's sweep the attempt is still
        // stored as InProgress. The read model must report that truthfully rather than
        // mutate state on a GET; the client detects it from DeadlineAt vs ServerTime.
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Late read", timeLimitMinutes: 30);
        var attempt = NewAttempt(revision, deadline: BaseTime.AddMinutes(30));
        await SeedAsync(database, ct, test, revision, attempt);

        await using var db = database.CreateContext();
        var view = await new ReadModelQueries(db)
            .GetAttemptPresentationAsync(attempt.Id, Student, BaseTime.AddHours(1), ct);

        Assert.Equal(AttemptStatus.InProgress, view!.Status);
        Assert.True(view.DeadlineAt < view.ServerTime);

        await using var verify = database.CreateContext();
        var stored = await verify.Attempts.SingleAsync(x => x.Id == attempt.Id, ct);
        Assert.Equal(AttemptStatus.InProgress, stored.Status);
    }

    // ------------------------------------------------------------------ ownership

    [Fact]
    public async Task Presentation_is_withheld_from_another_student()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Ownership");
        var attempt = NewAttempt(revision);
        await SeedAsync(database, ct, test, revision, attempt);

        await using var db = database.CreateContext();
        var queries = new ReadModelQueries(db);

        Assert.NotNull(await queries.GetAttemptPresentationAsync(attempt.Id, Student, BaseTime, ct));
        Assert.Null(await queries.GetAttemptPresentationAsync(attempt.Id, OtherStudent, BaseTime, ct));
    }

    // --------------------------------------------------------------------- resume

    [Fact]
    public async Task Resume_returns_the_in_progress_attempt_for_the_assignment()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Resume");
        var assignmentId = TestAssignmentId.New();
        var attempt = NewAttempt(revision, assignmentId: assignmentId);
        await SeedAsync(database, ct, test, revision, attempt);

        await using var db = database.CreateContext();
        var view = await new ReadModelQueries(db)
            .GetActiveAttemptPresentationAsync(assignmentId, Student, BaseTime, ct);

        Assert.NotNull(view);
        Assert.Equal(attempt.Id, view.AttemptId);
        Assert.Equal(assignmentId, view.AssignmentId);
    }

    [Fact]
    public async Task Resume_does_not_offer_a_completed_attempt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Completed resume");
        var assignmentId = TestAssignmentId.New();
        var attempt = NewAttempt(revision, assignmentId: assignmentId);
        var score = revision.CalculateScore(attempt.Responses);
        Assert.False(attempt.Submit(BaseTime.AddMinutes(5), score, revision.IsPassed(score)).TryGetError(out _));
        attempt.ClearDomainEvents();
        await SeedAsync(database, ct, test, revision, attempt);

        await using var db = database.CreateContext();
        var view = await new ReadModelQueries(db)
            .GetActiveAttemptPresentationAsync(assignmentId, Student, BaseTime, ct);

        Assert.Null(view);
    }

    [Fact]
    public async Task Resume_is_scoped_to_the_calling_student()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Resume scope");
        var assignmentId = TestAssignmentId.New();
        var attempt = NewAttempt(revision, assignmentId: assignmentId);
        await SeedAsync(database, ct, test, revision, attempt);

        await using var db = database.CreateContext();
        var view = await new ReadModelQueries(db)
            .GetActiveAttemptPresentationAsync(assignmentId, OtherStudent, BaseTime, ct);

        Assert.Null(view);
    }

    [Fact]
    public async Task Resume_ignores_an_in_progress_attempt_on_a_different_assignment()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Resume isolation");
        var attempt = NewAttempt(revision, assignmentId: TestAssignmentId.New());
        await SeedAsync(database, ct, test, revision, attempt);

        await using var db = database.CreateContext();
        var view = await new ReadModelQueries(db)
            .GetActiveAttemptPresentationAsync(TestAssignmentId.New(), Student, BaseTime, ct);

        Assert.Null(view);
    }

    // ------------------------------------------------- answer-key leakage contract

    [Fact]
    public async Task The_serialized_student_payload_contains_no_correctness_information()
    {
        // Load-bearing regression: asserts on the actual HTTP bytes. The correct option's
        // text must be present (the student has to be able to pick it) while nothing
        // indicating WHICH option is correct may appear anywhere in the payload.
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Leakage");
        var attempt = NewAttempt(revision);
        await SeedAsync(database, ct, test, revision, attempt);

        await using var factory = ApiTestHost.Create(database.ConnectionString);
        using var client = factory.CreateClient();
        ApiTestHost.Authenticate(client, Student.Value);

        using var response = await client.GetAsync($"/api/v1/attempts/{attempt.Id.Value}/presentation", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync(ct);

        Assert.Contains("Correct", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("isCorrect", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctOption", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", payload, StringComparison.OrdinalIgnoreCase);

        // Structural check as well as textual: no boolean anywhere under an option.
        using var document = JsonDocument.Parse(payload);
        foreach (var question in document.RootElement.GetProperty("questions").EnumerateArray())
        {
            foreach (var option in question.GetProperty("options").EnumerateArray())
            {
                Assert.All(
                    option.EnumerateObject(),
                    property => Assert.True(
                        property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False),
                        $"Student-facing option exposed boolean property '{property.Name}'."));
            }
        }
    }

    [Fact]
    public async Task Presentation_over_http_is_not_readable_by_another_student()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Http ownership");
        var attempt = NewAttempt(revision);
        await SeedAsync(database, ct, test, revision, attempt);

        await using var factory = ApiTestHost.Create(database.ConnectionString);
        using var client = factory.CreateClient();
        ApiTestHost.Authenticate(client, OtherStudent.Value);

        using var response = await client.GetAsync($"/api/v1/attempts/{attempt.Id.Value}/presentation", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Resume_over_http_answers_404_when_there_is_nothing_to_resume()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using (var setup = database.CreateContext())
            await setup.Database.MigrateAsync(ct);

        await using var factory = ApiTestHost.Create(database.ConnectionString);
        using var client = factory.CreateClient();
        ApiTestHost.Authenticate(client, Student.Value);

        using var response = await client.GetAsync(
            $"/api/v1/assignments/{Guid.CreateVersion7()}/attempts/active", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Resume_over_http_returns_the_attempt_a_client_can_pick_back_up()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var (test, revision) = CreatePublishedTest("Http resume");
        var assignmentId = TestAssignmentId.New();
        var attempt = NewAttempt(revision, assignmentId: assignmentId);
        await SeedAsync(database, ct, test, revision, attempt);

        await using var factory = ApiTestHost.Create(database.ConnectionString);
        using var client = factory.CreateClient();
        ApiTestHost.Authenticate(client, Student.Value);

        var view = await client.GetFromJsonAsync<AttemptPresentationView>(
            $"/api/v1/assignments/{assignmentId.Value}/attempts/active", ct);

        Assert.NotNull(view);
        Assert.Equal(attempt.Id, view.AttemptId);
        Assert.Single(view.Questions);
    }

    // ------------------------------------------------------------------- helpers

    private static async Task SeedAsync(
        PostgreSqlTestDatabase database,
        CancellationToken ct,
        Test test,
        PublishedTestRevision revision,
        TestAttempt attempt)
    {
        await using var setup = database.CreateContext();
        await setup.Database.MigrateAsync(ct);
        setup.Tests.Add(test);
        setup.Revisions.Add(revision);
        setup.Attempts.Add(attempt);
        await setup.SaveChangesAsync(ct);
    }

    private static TestAttempt NewAttempt(
        PublishedTestRevision revision,
        TestAssignmentId? assignmentId = null,
        DateTimeOffset? deadline = null)
    {
        var attempt = TestAttempt.Start(
            TestAttemptId.New(),
            assignmentId ?? TestAssignmentId.New(),
            revision.Id,
            Student,
            Guid.CreateVersion7(),
            BaseTime,
            deadline,
            revision.Questions.Select(q => q.Id));
        attempt.ClearDomainEvents();
        return attempt;
    }

    private static (Test Test, PublishedTestRevision Revision) CreatePublishedTest(
        string title,
        int? timeLimitMinutes = null)
    {
        var test = Test.Create(title, ExternalUserId.FromSubject("author-1"))
            .Match(t => t, error => throw new Xunit.Sdk.XunitException(error.Message));
        _ = test.ChangeSettings(50m, timeLimitMinutes);
        var questionId = test.AddQuestion("Pick one", QuestionType.SingleChoice, 1m, 1)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        test.AddAnswerOption(questionId, "Correct", true, 1);
        test.AddAnswerOption(questionId, "Wrong", false, 2);
        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, BaseTime.AddDays(-1));
        test.ClearDomainEvents();
        return (test, revision);
    }
}
