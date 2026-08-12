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

    public async Task<IReadOnlyList<AssignmentSummary>> GetAssignmentsAsync(
        ExternalUserId userId,
        IReadOnlySet<ExternalGroupId> groups,
        CancellationToken ct)
    {
        var assignments = await db.Assignments
            .AsNoTracking()
            .Where(x => x.Status == AssignmentStatus.Active)
            .ToListAsync(ct);

        var visible = assignments.Where(a => a.Target switch
        {
            AssignmentTarget.User user => user.UserId == userId,
            AssignmentTarget.Group group => groups.Contains(group.GroupId),
            _ => false
        }).ToArray();

        var revisionIds = visible.Select(x => x.RevisionId).Distinct().ToArray();
        var revisions = await db.Revisions
            .AsNoTracking()
            .Where(x => revisionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        return visible
            .Where(a => revisions.ContainsKey(a.RevisionId))
            .OrderBy(a => a.AvailableFrom)
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
