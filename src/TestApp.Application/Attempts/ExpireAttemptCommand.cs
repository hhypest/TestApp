using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Attempts;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Attempts;

public sealed record ExpireAttemptCommand(TestAttemptId AttemptId)
    : ICommand<Result<AttemptScore, Error>>;

public sealed class ExpireAttemptCommandHandler(
    ITestAttemptRepository attempts,
    IPublishedTestRevisionRepository revisions,
    IClock clock,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ExpireAttemptCommand, Result<AttemptScore, Error>>
{
    public async Task<Result<AttemptScore, Error>> Handle(ExpireAttemptCommand command, CancellationToken ct)
    {
        var attempt = await attempts.GetAsync(command.AttemptId, ct);
        if (attempt is null)
            return Error.NotFound("attempt.not_found", "Attempt was not found.");

        if (attempt.Status != AttemptStatus.InProgress)
        {
            if (attempt.Score is not null)
                return attempt.Score;

            return Error.Conflict("attempt.completed", "The attempt is already completed.");
        }

        // The deadline rule itself lives in TestAttempt.Timeout (ADR-030); this is only an early-out that
        // avoids loading the revision for the attempts the background sweep picked up too eagerly.
        var now = clock.UtcNow;
        if (!attempt.IsExpiredAt(now))
            return Error.Conflict("attempt.not_expired", "The attempt deadline has not expired yet.");

        var revision = await revisions.GetAsync(attempt.RevisionId, ct);
        if (revision is null)
            return Error.NotFound("revision.not_found", "Published test revision was not found.");

        var score = revision.CalculateScore(attempt.Responses);
        var result = attempt.Timeout(now, score, revision.IsPassed(score));
        var error = result.Match<Error?>(_ => null, domainError => domainError.ToApplicationError());
        if (error is not null)
            return error;

        await unitOfWork.SaveChangesAsync(ct);
        return score;
    }
}
