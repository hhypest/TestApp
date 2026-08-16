using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Attempts;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Attempts;

public sealed record AnswerQuestionCommand(TestAttemptId AttemptId, QuestionId QuestionId, IReadOnlyCollection<AnswerOptionId> OptionIds)
    : ICommand<Result<TestAttemptId, Error>>;
public sealed record ClearAnswerCommand(TestAttemptId AttemptId, QuestionId QuestionId)
    : ICommand<Result<TestAttemptId, Error>>;
public sealed record SubmitAttemptCommand(TestAttemptId AttemptId, Guid IdempotencyKey)
    : ICommand<Result<AttemptScore, Error>>;
public sealed record TimeoutAttemptCommand(TestAttemptId AttemptId)
    : ICommand<Result<AttemptScore, Error>>;

internal readonly record struct CachedAttemptScore(decimal Earned, decimal Maximum)
{
    public AttemptScore ToDomain() => new(Earned, Maximum);
    public static CachedAttemptScore FromDomain(AttemptScore score) => new(score.Earned, score.Maximum);
}

public sealed class AnswerQuestionCommandHandler(
    ITestAttemptRepository attempts,
    IPublishedTestRevisionRepository revisions,
    ICurrentActor actor,
    IClock clock,
    IUnitOfWork unitOfWork)
    : ICommandHandler<AnswerQuestionCommand, Result<TestAttemptId, Error>>
{
    public async Task<Result<TestAttemptId, Error>> Handle(AnswerQuestionCommand command, CancellationToken ct)
    {
        var attempt = await attempts.GetAsync(command.AttemptId, ct);
        if (attempt is null) return Error.NotFound("attempt.not_found", "Attempt was not found.");
        if (attempt.UserId != actor.UserId) return Error.Forbidden("attempt.forbidden", "Attempt belongs to another user.");
        var revision = await revisions.GetAsync(attempt.RevisionId, ct);
        if (revision is null) return Error.NotFound("revision.not_found", "Published test revision was not found.");
        var now = clock.UtcNow;
        if (attempt.IsExpiredAt(now))
        {
            var score = revision.CalculateScore(attempt.Responses);
            var timedOut = attempt.Timeout(now, score, revision.IsPassed(score));
            var timeoutError = timedOut.Match<Error?>(_ => null, e => e.ToApplicationError());
            if (timeoutError is null) await unitOfWork.SaveChangesAsync(ct);
            return Error.Conflict("attempt.expired", "The attempt deadline has expired.");
        }
        var validation = revision.ValidateAnswer(command.QuestionId, command.OptionIds);
        var validationError = validation.Match<Error?>(_ => null, error => error.ToApplicationError());
        if (validationError is not null) return validationError;
        var result = attempt.Answer(command.QuestionId, command.OptionIds, now);
        var error = result.Match<Error?>(_ => null, e => e.ToApplicationError());
        if (error is not null) return error;
        await unitOfWork.SaveChangesAsync(ct);
        return attempt.Id;
    }
}

public sealed class ClearAnswerCommandHandler(
    ITestAttemptRepository attempts,
    IPublishedTestRevisionRepository revisions,
    ICurrentActor actor,
    IClock clock,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ClearAnswerCommand, Result<TestAttemptId, Error>>
{
    public async Task<Result<TestAttemptId, Error>> Handle(ClearAnswerCommand command, CancellationToken ct)
    {
        var attempt = await attempts.GetAsync(command.AttemptId, ct);
        if (attempt is null) return Error.NotFound("attempt.not_found", "Attempt was not found.");
        if (attempt.UserId != actor.UserId) return Error.Forbidden("attempt.forbidden", "Attempt belongs to another user.");
        var now = clock.UtcNow;
        if (attempt.IsExpiredAt(now))
        {
            var revision = await revisions.GetAsync(attempt.RevisionId, ct);
            if (revision is null) return Error.NotFound("revision.not_found", "Published test revision was not found.");
            var score = revision.CalculateScore(attempt.Responses);
            var timedOut = attempt.Timeout(now, score, revision.IsPassed(score));
            if (timedOut.IsSuccess) await unitOfWork.SaveChangesAsync(ct);
            return Error.Conflict("attempt.expired", "The attempt deadline has expired.");
        }
        var result = attempt.ClearAnswer(command.QuestionId, now);
        var error = result.Match<Error?>(_ => null, e => e.ToApplicationError());
        if (error is not null) return error;
        await unitOfWork.SaveChangesAsync(ct);
        return attempt.Id;
    }
}

public sealed class SubmitAttemptCommandHandler(
    ITestAttemptRepository attempts,
    IPublishedTestRevisionRepository revisions,
    ICurrentActor actor,
    IClock clock,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SubmitAttemptCommand, Result<AttemptScore, Error>>
{
    public async Task<Result<AttemptScore, Error>> Handle(SubmitAttemptCommand command, CancellationToken ct)
    {
        if (command.IdempotencyKey == Guid.Empty)
            return Error.Validation("idempotency.key", "Idempotency key is required.");

        var operation = $"attempt.submit:{command.AttemptId.Value:N}";
        var fingerprint = IdempotencyFingerprint.Create(IdempotencyFingerprint.Guid(command.AttemptId.Value));
        var cached = await idempotency.GetResultAsync<CachedAttemptScore>(operation, actor.UserId, command.IdempotencyKey, fingerprint, ct);
        if (cached is { } cachedScore)
            return cachedScore.ToDomain();

        await using var lease = await idempotency.AcquireAsync(operation, actor.UserId, command.IdempotencyKey, ct);

        cached = await idempotency.GetResultAsync<CachedAttemptScore>(operation, actor.UserId, command.IdempotencyKey, fingerprint, ct);
        if (cached is { } leasedCachedScore)
            return leasedCachedScore.ToDomain();

        var attempt = await attempts.GetAsync(command.AttemptId, ct);
        if (attempt is null) return Error.NotFound("attempt.not_found", "Attempt was not found.");
        if (attempt.UserId != actor.UserId) return Error.Forbidden("attempt.forbidden", "Attempt belongs to another user.");
        var revision = await revisions.GetAsync(attempt.RevisionId, ct);
        if (revision is null) return Error.NotFound("revision.not_found", "Published test revision was not found.");
        var now = clock.UtcNow;
        var score = revision.CalculateScore(attempt.Responses);
        var result = attempt.IsExpiredAt(now)
            ? attempt.Timeout(now, score, revision.IsPassed(score))
            : attempt.Submit(now, score, revision.IsPassed(score));
        var error = result.Match<Error?>(_ => null, e => e.ToApplicationError());
        if (error is not null) return error;

        await idempotency.AddResultAsync(
            operation,
            actor.UserId,
            command.IdempotencyKey,
            fingerprint,
            CachedAttemptScore.FromDomain(score),
            now,
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        return score;
    }
}

public sealed class TimeoutAttemptCommandHandler(
    ITestAttemptRepository attempts,
    IPublishedTestRevisionRepository revisions,
    IClock clock,
    IUnitOfWork unitOfWork)
    : ICommandHandler<TimeoutAttemptCommand, Result<AttemptScore, Error>>
{
    public async Task<Result<AttemptScore, Error>> Handle(TimeoutAttemptCommand command, CancellationToken ct)
    {
        var attempt = await attempts.GetAsync(command.AttemptId, ct);
        if (attempt is null) return Error.NotFound("attempt.not_found", "Attempt was not found.");
        var revision = await revisions.GetAsync(attempt.RevisionId, ct);
        if (revision is null) return Error.NotFound("revision.not_found", "Published test revision was not found.");
        var score = revision.CalculateScore(attempt.Responses);
        // Manual administrator timeout: the operator's authority, not the deadline (ADR-030).
        var result = attempt.ForceTimeout(clock.UtcNow, score, revision.IsPassed(score));
        var error = result.Match<Error?>(_ => null, e => e.ToApplicationError());
        if (error is not null) return error;
        await unitOfWork.SaveChangesAsync(ct);
        return score;
    }
}
