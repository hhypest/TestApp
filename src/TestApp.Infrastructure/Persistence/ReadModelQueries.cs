using Microsoft.EntityFrameworkCore;
using TestApp.Application.Queries;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Infrastructure.Persistence;

public sealed class ReadModelQueries(AppDbContext db) : IReadModelQueries
{
    public async Task<TestEditorView?> GetTestEditorViewAsync(TestId testId, CancellationToken ct)
    {
        var test = await db.Tests.AsNoTracking().Include(x => x.Questions).ThenInclude(x => x.Options).SingleOrDefaultAsync(x => x.Id == testId, ct);
        if (test is null) return null;

        return new TestEditorView(
            test.Id,
            test.Title,
            test.Status,
            test.Settings.PassingPercentage,
            test.Settings.TimeLimitMinutes,
            test.Questions.OrderBy(q => q.Order).Select(q => new QuestionEditorView(
                q.Id,
                q.Text,
                q.Type,
                q.Points,
                q.Order,
                q.Options.OrderBy(o => o.Order).Select(o => new AnswerOptionEditorView(o.Id, o.Text, o.IsCorrect, o.Order)).ToArray())).ToArray());
    }

    public async Task<PagedResult<AssignmentSummary>> GetAssignmentsAsync(
        ExternalUserId userId,
        IReadOnlySet<ExternalGroupId> groups,
        int page,
        int pageSize,
        AssignmentStatus? status,
        CancellationToken ct)
    {
        var groupIds = groups.Select(x => x.Value).ToArray();
        var query = db.Assignments.AsNoTracking().Where(x =>
            (x.TargetType == AssignmentTargetType.User && x.TargetId == userId.Value) ||
            (x.TargetType == AssignmentTargetType.Group && groupIds.Contains(x.TargetId)));
        if (status is { } statusValue)
            query = query.Where(x => x.Status == statusValue);

        var totalCount = await query.CountAsync(ct);
        var pageItems = await query.OrderByDescending(a => a.AssignedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var revisionIds = pageItems.Select(x => x.RevisionId).Distinct().ToArray();
        var revisions = await db.Revisions.AsNoTracking().Where(x => revisionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

        var items = pageItems.Where(a => revisions.ContainsKey(a.RevisionId)).Select(a =>
        {
            var revision = revisions[a.RevisionId];
            return new AssignmentSummary(
                a.Id,
                a.RevisionId,
                revision.Title,
                revision.Version,
                revision.PassingPercentage,
                revision.TimeLimitMinutes,
                a.AvailableFrom,
                a.AvailableUntil,
                a.AttemptLimit,
                a.Status);
        }).ToArray();

        return new PagedResult<AssignmentSummary>(items, page, pageSize, totalCount);
    }

    public async Task<PagedResult<AttemptSummary>> GetAttemptsAsync(ExternalUserId userId, int page, int pageSize, AttemptStatus? status, CancellationToken ct)
    {
        var query = db.Attempts.AsNoTracking().Where(x => x.UserId == userId);
        if (status is { } value)
            query = query.Where(x => x.Status == value);

        var totalCount = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.StartedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(attempt => new AttemptSummary(
            attempt.Id,
            attempt.AssignmentId,
            attempt.RevisionId,
            attempt.Status,
            attempt.Outcome,
            attempt.Score?.Percentage,
            attempt.StartedAt,
            attempt.DeadlineAt,
            attempt.CompletedAt)).ToArray();

        return new PagedResult<AttemptSummary>(items, page, pageSize, totalCount);
    }

    public async Task<PagedResult<ReviewerResultSummary>> GetReviewerResultsAsync(
        TestId? testId,
        PublishedTestRevisionId? revisionId,
        AttemptOutcome? outcome,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var revisionQuery = db.Revisions.AsNoTracking();
        if (testId is { } testValue)
            revisionQuery = revisionQuery.Where(x => x.TestId == testValue);
        if (revisionId is { } revisionValue)
            revisionQuery = revisionQuery.Where(x => x.Id == revisionValue);

        var revisionIds = await revisionQuery.Select(x => x.Id).ToArrayAsync(ct);
        var attemptsQuery = db.Attempts.AsNoTracking().Where(x => revisionIds.Contains(x.RevisionId));
        if (outcome is { } outcomeValue)
            attemptsQuery = attemptsQuery.Where(x => x.Outcome == outcomeValue);

        var totalCount = await attemptsQuery.CountAsync(ct);
        var attempts = await attemptsQuery.OrderByDescending(x => x.StartedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var pageRevisionIds = attempts.Select(x => x.RevisionId).Distinct().ToArray();
        var revisions = await db.Revisions.AsNoTracking().Where(x => pageRevisionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

        var items = attempts.Where(a => revisions.ContainsKey(a.RevisionId)).Select(a =>
        {
            var revision = revisions[a.RevisionId];
            return new ReviewerResultSummary(
                a.Id,
                a.AssignmentId,
                a.RevisionId,
                revision.TestId,
                revision.Title,
                revision.Version,
                a.UserId,
                a.Status,
                a.Outcome,
                a.Score?.Earned,
                a.Score?.Maximum,
                a.Score?.Percentage,
                a.StartedAt,
                a.DeadlineAt,
                a.CompletedAt);
        }).ToArray();

        return new PagedResult<ReviewerResultSummary>(items, page, pageSize, totalCount);
    }

    public async Task<AttemptView?> GetAttemptAsync(TestAttemptId attemptId, ExternalUserId userId, CancellationToken ct)
    {
        var attempt = await db.Attempts.AsNoTracking().Include(x => x.Responses).ThenInclude(x => x.SelectedOptions).SingleOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, ct);
        if (attempt is null) return null;

        return new AttemptView(
            attempt.Id,
            attempt.AssignmentId,
            attempt.RevisionId,
            attempt.Status,
            attempt.StartedAt,
            attempt.DeadlineAt,
            attempt.CompletedAt,
            attempt.Outcome,
            attempt.Responses.Select(r => new QuestionResponseView(r.Id, r.SelectedOptions.Select(x => x.OptionId).ToArray(), r.AnsweredAt)).ToArray());
    }

    public async Task<AttemptResultView?> GetAttemptResultAsync(TestAttemptId attemptId, ExternalUserId userId, CancellationToken ct)
    {
        var attempt = await db.Attempts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, ct);
        if (attempt is null) return null;

        return new AttemptResultView(
            attempt.Id,
            attempt.RevisionId,
            attempt.Status,
            attempt.Outcome,
            attempt.Score?.Earned,
            attempt.Score?.Maximum,
            attempt.Score?.Percentage,
            attempt.StartedAt,
            attempt.DeadlineAt,
            attempt.CompletedAt);
    }
}
