using Microsoft.EntityFrameworkCore;
using TestApp.Application.Queries;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;

namespace TestApp.Infrastructure.Persistence;

public sealed partial class ReadModelQueries
{
    public async Task<PagedResult<AttemptSummary>> GetAttemptsAsync(
        ExternalUserId userId,
        int page,
        int pageSize,
        AttemptStatus? status,
        CancellationToken ct)
    {
        var query = _db.Attempts.AsNoTracking().Where(attempt => attempt.UserId == userId);
        if (status is { } value)
            query = query.Where(attempt => attempt.Status == value);

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(attempt => attempt.StartedAt)
            .ThenByDescending(attempt => attempt.Id)
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

    public async Task<AttemptView?> GetAttemptAsync(
        TestAttemptId attemptId,
        ExternalUserId userId,
        CancellationToken ct)
    {
        var attempt = await _db.Attempts
            .AsNoTracking()
            .Include(value => value.Responses)
            .ThenInclude(response => response.SelectedOptions)
            .SingleOrDefaultAsync(value => value.Id == attemptId && value.UserId == userId, ct);
        if (attempt is null)
            return null;

        return new AttemptView(
            attempt.Id,
            attempt.AssignmentId,
            attempt.RevisionId,
            attempt.Status,
            attempt.StartedAt,
            attempt.DeadlineAt,
            attempt.CompletedAt,
            attempt.Outcome,
            attempt.Responses.Select(response => new QuestionResponseView(
                response.Id,
                response.SelectedOptions.Select(option => option.OptionId).ToArray(),
                response.AnsweredAt)).ToArray());
    }

    public async Task<AttemptResultView?> GetAttemptResultAsync(
        TestAttemptId attemptId,
        ExternalUserId userId,
        CancellationToken ct)
    {
        var attempt = await _db.Attempts
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == attemptId && value.UserId == userId, ct);
        if (attempt is null)
            return null;

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
