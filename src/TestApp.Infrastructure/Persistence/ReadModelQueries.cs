using Microsoft.EntityFrameworkCore;
using TestApp.Application.Queries;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Tests;

namespace TestApp.Infrastructure.Persistence;

public sealed class ReadModelQueries(AppDbContext db) : IReadModelQueries
{
    public async Task<TestEditorView?> GetTestEditorViewAsync(TestId testId, CancellationToken ct)
    {
        var test = await db.Tests
            .AsNoTracking()
            .Include(x => x.Questions)
            .ThenInclude(x => x.Options)
            .SingleOrDefaultAsync(x => x.Id == testId, ct);

        if (test is null) return null;

        return new TestEditorView(
            test.Id,
            test.Title,
            test.Status,
            test.Settings.PassingPercentage,
            test.Settings.TimeLimitMinutes,
            test.Questions
                .OrderBy(q => q.Order)
                .Select(q => new QuestionEditorView(
                    q.Id,
                    q.Text,
                    q.Type,
                    q.Points,
                    q.Order,
                    q.Options.OrderBy(o => o.Order)
                        .Select(o => new AnswerOptionEditorView(o.Id, o.Text, o.IsCorrect, o.Order))
                        .ToArray()))
                .ToArray());
    }

    public async Task<PagedResult<AssignmentSummary>> GetAssignmentsAsync(
        ExternalUserId userId,
        IReadOnlySet<ExternalGroupId> groups,
        int page,
        int pageSize,
        AssignmentStatus? status,
        CancellationToken ct)
    {
        var assignments = await db.Assignments
            .AsNoTracking()
            .Where(x => status == null || x.Status == status)
            .ToListAsync(ct);

        var visible = assignments.Where(a => a.Target switch
        {
            AssignmentTarget.User user => user.UserId == userId,
            AssignmentTarget.Group group => groups.Contains(group.GroupId),
            _ => false
        }).OrderByDescending(a => a.AssignedAt).ToArray();

        var totalCount = visible.Length;
        var pageItems = visible.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var revisionIds = pageItems.Select(x => x.RevisionId).Distinct().ToArray();
        var revisions = await db.Revisions
            .AsNoTracking()
            .Where(x => revisionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var items = pageItems
            .Where(a => revisions.ContainsKey(a.RevisionId))
            .Select(a =>
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
            })
            .ToArray();

        return new PagedResult<AssignmentSummary>(items, page, pageSize, totalCount);
    }

    public async Task<PagedResult<AttemptSummary>> GetAttemptsAsync(
        ExternalUserId userId,
        int page,
        int pageSize,
        AttemptStatus? status,
        CancellationToken ct)
    {
        var query = db.Attempts.AsNoTracking().Where(x => x.UserId == userId);
        if (status is { } value)
            query = query.Where(x => x.Status == value);

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

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

    public async Task<AttemptView?> GetAttemptAsync(TestAttemptId attemptId, ExternalUserId userId, CancellationToken ct)
    {
        var attempt = await db.Attempts
            .AsNoTracking()
            .Include(x => x.Responses)
            .ThenInclude(x => x.SelectedOptions)
            .SingleOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, ct);

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
            attempt.Responses
                .Select(r => new QuestionResponseView(
                    r.Id,
                    r.SelectedOptions.Select(x => x.OptionId).ToArray(),
                    r.AnsweredAt))
                .ToArray());
    }

    public async Task<AttemptResultView?> GetAttemptResultAsync(TestAttemptId attemptId, ExternalUserId userId, CancellationToken ct)
    {
        var attempt = await db.Attempts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, ct);

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
