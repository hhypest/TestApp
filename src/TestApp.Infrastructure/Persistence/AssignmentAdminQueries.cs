using Microsoft.EntityFrameworkCore;
using TestApp.Application.Queries;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Infrastructure.Persistence;

public sealed class AssignmentAdminQueries(AppDbContext db) : IAssignmentAdminQueries
{
    public async Task<PagedResult<AdminAssignmentSummary>> GetAssignmentsAsync(
        TestId? testId,
        PublishedTestRevisionId? revisionId,
        AssignmentTargetType? targetType,
        string? targetId,
        AssignmentStatus? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = db.Assignments.AsNoTracking().AsQueryable();
        if (testId is { } testValue)
            query = query.Where(x => db.Revisions.Any(revision =>
                revision.Id == x.RevisionId && revision.TestId == testValue));
        if (revisionId is { } revisionValue)
            query = query.Where(x => x.RevisionId == revisionValue);
        if (targetType is { } targetTypeValue)
            query = query.Where(x => x.TargetType == targetTypeValue);
        if (!string.IsNullOrWhiteSpace(targetId))
            query = query.Where(x => x.TargetId == targetId);
        if (status is { } statusValue)
            query = query.Where(x => x.Status == statusValue);

        var totalCount = await query.CountAsync(ct);
        var assignments = await query
            .OrderByDescending(x => x.AssignedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var pageRevisionIds = assignments.Select(x => x.RevisionId).Distinct().ToArray();
        var revisionMap = await db.Revisions.AsNoTracking()
            .Where(x => pageRevisionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var assignmentIds = assignments.Select(x => x.Id).ToArray();
        var attemptStats = await db.Attempts.AsNoTracking()
            .Where(x => assignmentIds.Contains(x.AssignmentId))
            .GroupBy(x => x.AssignmentId)
            .Select(g => new
            {
                AssignmentId = g.Key,
                AttemptCount = g.Count(),
                InProgressCount = g.Count(x => x.Status == AttemptStatus.InProgress),
                CompletedCount = g.Count(x => x.Status != AttemptStatus.InProgress),
                PassedCount = g.Count(x => x.Outcome == AttemptOutcome.Passed),
                FailedCount = g.Count(x => x.Outcome == AttemptOutcome.Failed)
            })
            .ToDictionaryAsync(x => x.AssignmentId, ct);

        var scoreRows = await db.Attempts.AsNoTracking()
            .Where(x => assignmentIds.Contains(x.AssignmentId) && x.Score != null)
            .Select(x => new { x.AssignmentId, x.Score!.Earned, x.Score.Maximum })
            .ToListAsync(ct);
        var scoreStats = scoreRows
            .GroupBy(x => x.AssignmentId)
            .ToDictionary(
                g => g.Key,
                g => (decimal?)Math.Round(g.Average(x => Percentage(x.Earned, x.Maximum)), 2));

        var items = assignments.Where(x => revisionMap.ContainsKey(x.RevisionId)).Select(assignment =>
        {
            var revision = revisionMap[assignment.RevisionId];
            attemptStats.TryGetValue(assignment.Id, out var stats);
            scoreStats.TryGetValue(assignment.Id, out var averagePercentage);
            return new AdminAssignmentSummary(
                assignment.Id,
                assignment.RevisionId,
                revision.TestId,
                revision.Title,
                revision.Version,
                assignment.TargetType,
                assignment.TargetId,
                assignment.AssignedBy,
                assignment.AssignedAt,
                assignment.AvailableFrom,
                assignment.AvailableUntil,
                assignment.AttemptLimit,
                assignment.Status,
                stats?.AttemptCount ?? 0,
                stats?.InProgressCount ?? 0,
                stats?.CompletedCount ?? 0,
                stats?.PassedCount ?? 0,
                stats?.FailedCount ?? 0,
                averagePercentage);
        }).ToArray();

        return new PagedResult<AdminAssignmentSummary>(items, page, pageSize, totalCount);
    }

    public async Task<AdminAssignmentDetail?> GetAssignmentAsync(TestAssignmentId assignmentId, CancellationToken ct)
    {
        var assignment = await db.Assignments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (assignment is null) return null;

        var revision = await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assignment.RevisionId, ct);
        if (revision is null) return null;

        var attempts = db.Attempts.AsNoTracking().Where(x => x.AssignmentId == assignmentId);
        var attemptCount = await attempts.CountAsync(ct);
        var inProgressCount = await attempts.CountAsync(x => x.Status == AttemptStatus.InProgress, ct);
        var submittedCount = await attempts.CountAsync(x => x.Status == AttemptStatus.Submitted, ct);
        var timedOutCount = await attempts.CountAsync(x => x.Status == AttemptStatus.TimedOut, ct);
        var passedCount = await attempts.CountAsync(x => x.Outcome == AttemptOutcome.Passed, ct);
        var failedCount = await attempts.CountAsync(x => x.Outcome == AttemptOutcome.Failed, ct);
        var scoreRows = await attempts
            .Where(x => x.Score != null)
            .Select(x => new { x.Score!.Earned, x.Score.Maximum })
            .ToListAsync(ct);
        var percentages = scoreRows.Select(x => Percentage(x.Earned, x.Maximum)).ToArray();
        decimal? averagePercentage = percentages.Length == 0 ? null : Math.Round(percentages.Average(), 2);
        decimal? bestPercentage = percentages.Length == 0 ? null : percentages.Max();

        return new AdminAssignmentDetail(
            assignment.Id,
            assignment.RevisionId,
            revision.TestId,
            revision.Title,
            revision.Version,
            assignment.TargetType,
            assignment.TargetId,
            assignment.AssignedBy,
            assignment.AssignedAt,
            assignment.AvailableFrom,
            assignment.AvailableUntil,
            assignment.AttemptLimit,
            assignment.Status,
            assignment.CancelledBy,
            assignment.CancelledAt,
            assignment.CancelReason,
            attemptCount,
            inProgressCount,
            submittedCount,
            timedOutCount,
            passedCount,
            failedCount,
            averagePercentage,
            bestPercentage);
    }

    public async Task<PagedResult<AdminAssignmentAttemptSummary>?> GetAssignmentAttemptsAsync(
        TestAssignmentId assignmentId,
        AttemptStatus? status,
        AttemptOutcome? outcome,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (!await db.Assignments.AsNoTracking().AnyAsync(x => x.Id == assignmentId, ct))
            return null;

        var query = db.Attempts.AsNoTracking().Where(x => x.AssignmentId == assignmentId);
        if (status is { } statusValue)
            query = query.Where(x => x.Status == statusValue);
        if (outcome is { } outcomeValue)
            query = query.Where(x => x.Outcome == outcomeValue);

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rows.Select(attempt => new AdminAssignmentAttemptSummary(
            attempt.Id,
            attempt.UserId,
            attempt.Status,
            attempt.Outcome,
            attempt.Score?.Earned,
            attempt.Score?.Maximum,
            attempt.Score?.Percentage,
            attempt.StartedAt,
            attempt.DeadlineAt,
            attempt.CompletedAt)).ToArray();

        return new PagedResult<AdminAssignmentAttemptSummary>(items, page, pageSize, totalCount);
    }

    private static decimal Percentage(decimal earned, decimal maximum) =>
        maximum <= 0 ? 0 : Math.Round(earned / maximum * 100m, 2);
}
