using Microsoft.EntityFrameworkCore;
using TestApp.Application.Queries;
using TestApp.Domain.Assignments;
using TestApp.Domain.Identity;

namespace TestApp.Infrastructure.Persistence;

public sealed partial class ReadModelQueries
{
    public async Task<PagedResult<AssignmentSummary>> GetAssignmentsAsync(
        ExternalUserId userId,
        IReadOnlySet<ExternalGroupId> groups,
        int page,
        int pageSize,
        AssignmentStatus? status,
        CancellationToken ct)
    {
        var groupIds = groups.Select(group => group.Value).ToArray();
        var query = _db.Assignments.AsNoTracking().Where(assignment =>
            (assignment.TargetType == AssignmentTargetType.User && assignment.TargetId == userId.Value) ||
            (assignment.TargetType == AssignmentTargetType.Group && groupIds.Contains(assignment.TargetId)));
        if (status is { } statusValue)
            query = query.Where(assignment => assignment.Status == statusValue);

        var totalCount = await query.CountAsync(ct);
        var pageItems = await query
            .OrderByDescending(assignment => assignment.AssignedAt)
            .ThenByDescending(assignment => assignment.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var revisionIds = pageItems.Select(assignment => assignment.RevisionId).Distinct().ToArray();
        var revisions = await _db.Revisions
            .AsNoTracking()
            .Where(revision => revisionIds.Contains(revision.Id))
            .ToDictionaryAsync(revision => revision.Id, ct);

        var items = pageItems
            .Where(assignment => revisions.ContainsKey(assignment.RevisionId))
            .Select(assignment =>
            {
                var revision = revisions[assignment.RevisionId];
                return new AssignmentSummary(
                    assignment.Id,
                    assignment.RevisionId,
                    revision.Title,
                    revision.Version,
                    revision.PassingPercentage,
                    revision.TimeLimitMinutes,
                    assignment.AvailableFrom,
                    assignment.AvailableUntil,
                    assignment.AttemptLimit,
                    assignment.Status);
            })
            .ToArray();

        return new PagedResult<AssignmentSummary>(items, page, pageSize, totalCount);
    }
}
