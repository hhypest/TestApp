using Microsoft.EntityFrameworkCore;
using TestApp.Application.Queries;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Infrastructure.Persistence;

public sealed partial class ReadModelQueries
{
    public async Task<PagedResult<ReviewerResultSummary>> GetReviewerResultsAsync(
        TestId? testId,
        PublishedTestRevisionId? revisionId,
        AttemptOutcome? outcome,
        ExternalUserId? ownerId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var attemptsQuery = _db.Attempts.AsNoTracking().AsQueryable();
        if (revisionId is { } revisionValue)
            attemptsQuery = attemptsQuery.Where(attempt => attempt.RevisionId == revisionValue);
        if (testId is { } testValue)
            attemptsQuery = attemptsQuery.Where(attempt => _db.Revisions.Any(revision =>
                revision.Id == attempt.RevisionId && revision.TestId == testValue));
        if (ownerId is { } owner)
            attemptsQuery = attemptsQuery.Where(attempt => _db.Revisions.Any(revision =>
                revision.Id == attempt.RevisionId &&
                _db.Tests.Any(test => test.Id == revision.TestId && test.OwnerId == owner)));
        if (outcome is { } outcomeValue)
            attemptsQuery = attemptsQuery.Where(attempt => attempt.Outcome == outcomeValue);

        var totalCount = await attemptsQuery.CountAsync(ct);
        var attempts = await attemptsQuery
            .OrderByDescending(attempt => attempt.StartedAt)
            .ThenByDescending(attempt => attempt.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var pageRevisionIds = attempts.Select(attempt => attempt.RevisionId).Distinct().ToArray();
        var revisions = await _db.Revisions
            .AsNoTracking()
            .Where(revision => pageRevisionIds.Contains(revision.Id))
            .ToDictionaryAsync(revision => revision.Id, ct);

        var items = attempts
            .Where(attempt => revisions.ContainsKey(attempt.RevisionId))
            .Select(attempt =>
            {
                var revision = revisions[attempt.RevisionId];
                return new ReviewerResultSummary(
                    attempt.Id,
                    attempt.AssignmentId,
                    attempt.RevisionId,
                    revision.TestId,
                    revision.Title,
                    revision.Version,
                    attempt.UserId,
                    attempt.Status,
                    attempt.Outcome,
                    attempt.Score?.Earned,
                    attempt.Score?.Maximum,
                    attempt.Score?.Percentage,
                    attempt.StartedAt,
                    attempt.DeadlineAt,
                    attempt.CompletedAt);
            })
            .ToArray();

        return new PagedResult<ReviewerResultSummary>(items, page, pageSize, totalCount);
    }

    public async Task<ReviewerAttemptResultView?> GetReviewerAttemptResultAsync(
        TestAttemptId attemptId,
        ExternalUserId? ownerId,
        CancellationToken ct)
    {
        var attempt = await _db.Attempts
            .AsNoTracking()
            .Include(value => value.Responses)
            .ThenInclude(response => response.SelectedOptions)
            .SingleOrDefaultAsync(value => value.Id == attemptId, ct);
        if (attempt is null)
            return null;

        var revisionQuery = _db.Revisions.AsNoTracking().Where(revision => revision.Id == attempt.RevisionId);
        if (ownerId is { } owner)
            revisionQuery = revisionQuery.Where(revision =>
                _db.Tests.Any(test => test.Id == revision.TestId && test.OwnerId == owner));
        var revision = await revisionQuery.SingleOrDefaultAsync(ct);
        if (revision is null)
            return null;

        var responses = attempt.Responses.ToDictionary(response => response.Id);
        var questions = revision.Questions
            .OrderBy(question => question.Order)
            .Select(question =>
            {
                responses.TryGetValue(question.Id, out var response);
                var selectedIds = response?.SelectedOptions.Select(option => option.OptionId).ToHashSet() ?? [];
                var correctIds = question.Options
                    .Where(option => option.IsCorrect)
                    .Select(option => option.Id)
                    .ToHashSet();
                var earnedPoints = selectedIds.SetEquals(correctIds) ? question.Points : 0m;

                var options = question.Options
                    .OrderBy(option => option.Order)
                    .Select(option => new ReviewerAnswerOptionView(
                        option.Id,
                        option.Text,
                        option.Order,
                        option.IsCorrect,
                        selectedIds.Contains(option.Id)))
                    .ToArray();

                return new ReviewerQuestionResultView(
                    question.Id,
                    question.Text,
                    question.Type,
                    question.Points,
                    earnedPoints,
                    question.Order,
                    response?.AnsweredAt,
                    options);
            })
            .ToArray();

        return new ReviewerAttemptResultView(
            attempt.Id,
            attempt.AssignmentId,
            attempt.RevisionId,
            revision.TestId,
            revision.Title,
            revision.Version,
            attempt.UserId,
            attempt.Status,
            attempt.Outcome,
            attempt.Score?.Earned,
            attempt.Score?.Maximum,
            attempt.Score?.Percentage,
            attempt.StartedAt,
            attempt.DeadlineAt,
            attempt.CompletedAt,
            questions);
    }
}
