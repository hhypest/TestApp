using Microsoft.EntityFrameworkCore;
using TestApp.Application.Queries;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;

namespace TestApp.Infrastructure.Persistence;

public sealed partial class ReadModelQueries
{
    public async Task<AttemptPresentationView?> GetAttemptPresentationAsync(
        TestAttemptId attemptId,
        ExternalUserId userId,
        DateTimeOffset serverTime,
        CancellationToken ct)
    {
        var attempt = await OwnedAttempts(userId)
            .SingleOrDefaultAsync(value => value.Id == attemptId, ct);

        return attempt is null ? null : await ProjectAsync(attempt, serverTime, ct);
    }

    public async Task<AttemptPresentationView?> GetActiveAttemptPresentationAsync(
        TestAssignmentId assignmentId,
        ExternalUserId userId,
        DateTimeOffset serverTime,
        CancellationToken ct)
    {
        // "Resume" means the still-running attempt. A completed attempt is history and
        // is read through /result instead, so it must not be offered for resumption.
        // Ordering keeps the newest first purely defensively: the DB unique key on
        // (AssignmentId, UserId, StartRequestId) plus the attempt-limit lease make more
        // than one in-progress attempt unexpected rather than impossible.
        var attempt = await OwnedAttempts(userId)
            .Where(value => value.AssignmentId == assignmentId && value.Status == AttemptStatus.InProgress)
            .OrderByDescending(value => value.StartedAt)
            .ThenByDescending(value => value.Id)
            .FirstOrDefaultAsync(ct);

        return attempt is null ? null : await ProjectAsync(attempt, serverTime, ct);
    }

    private IQueryable<TestAttempt> OwnedAttempts(ExternalUserId userId) =>
        _db.Attempts
            .AsNoTracking()
            .Include(value => value.Responses)
            .ThenInclude(response => response.SelectedOptions)
            .Where(value => value.UserId == userId);

    /// <summary>
    /// Builds the student-facing view from the immutable revision the attempt was started
    /// against, merged with the student's own saved responses.
    /// </summary>
    /// <remarks>
    /// The revision's questions — including every option's correctness flag — are stored
    /// as a single <c>jsonb</c> column, so EF materializes the whole answer key here and
    /// no SQL-level projection can withhold it. Excluding correctness is therefore a
    /// property of this method alone, which is why it is pinned by a serialization-level
    /// regression test rather than only by unit assertions. See ADR-028.
    /// </remarks>
    private async Task<AttemptPresentationView?> ProjectAsync(
        TestAttempt attempt,
        DateTimeOffset serverTime,
        CancellationToken ct)
    {
        var revision = await _db.Revisions
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == attempt.RevisionId, ct);
        if (revision is null)
            return null;

        var responses = attempt.Responses.ToDictionary(response => response.Id);

        var questions = revision.Questions
            .OrderBy(question => question.Order)
            .Select(question =>
            {
                responses.TryGetValue(question.Id, out var response);
                return new AttemptQuestionView(
                    question.Id,
                    question.Text,
                    question.Type,
                    question.Points,
                    question.Order,
                    response?.AnsweredAt,
                    response?.SelectedOptions.Select(option => option.OptionId).ToArray() ?? [],
                    question.Options
                        .OrderBy(option => option.Order)
                        .Select(option => new AttemptAnswerOptionView(option.Id, option.Text, option.Order))
                        .ToArray());
            })
            .ToArray();

        return new AttemptPresentationView(
            attempt.Id,
            attempt.AssignmentId,
            attempt.RevisionId,
            revision.Title,
            revision.Version,
            revision.PassingPercentage,
            revision.TimeLimitMinutes,
            attempt.Status,
            attempt.Outcome,
            attempt.StartedAt,
            attempt.DeadlineAt,
            attempt.CompletedAt,
            serverTime,
            questions);
    }
}
