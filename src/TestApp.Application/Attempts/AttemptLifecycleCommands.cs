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

public sealed record SubmitAttemptCommand(TestAttemptId AttemptId)
    : ICommand<Result<AttemptScore, Error>>;

public sealed record TimeoutAttemptCommand(TestAttemptId AttemptId)
    : ICommand<Result<TestAttemptId, Error>>;

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

        var validation = revision.ValidateAnswer(command.QuestionId, command.OptionIds);
        var validationError = validation.Match<Error?>(_ => null, error => error.ToApplicationError());
        if (validationError is not null) return validationError;

        var result = attempt.Answer(command.QuestionId, command.OptionIds, clock.UtcNow);
        var error = result.Match<Error?>(_ => null, e => e.ToApplicationError());
        if (error is not null) return error;

        await unitOfWork.SaveChangesAsync(ct);
        return attempt.Id;
    }
}

public sealed class ClearAnswerCommandHandler(
    ITestAttemptRepository attempts,
    ICurrentActor actor,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ClearAnswerCommand, Result<TestAttemptId, Error>>
{
    public async Task<Result<TestAttemptId, Error>> Handle(ClearAnswerCommand command, CancellationToken ct)
    {
        var attempt = await attempts.GetAsync(command.AttemptId, ct);
        if (attempt is null) return Error.NotFound("attempt.not_found", "Attempt was not found.");
        if (attempt.UserId != actor.UserId) return Error.Forbidden("attempt.forbidden", "Attempt belongs to another user.");

        var result = attempt.ClearAnswer(command.QuestionId);
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
    IUnitOfWork unitOfWork)
    : ICommandHandler<SubmitAttemptCommand, Result<AttemptScore, Error>>
{
    public async Task<Result<AttemptScore, Error>> Handle(SubmitAttemptCommand command, CancellationToken ct)
    {
        var attempt = await attempts.GetAsync(command.AttemptId, ct);
        if (attempt is null) return Error.NotFound("attempt.not_found", "Attempt was not found.");
        if (attempt.UserId != actor.UserId) return Error.Forbidden("attempt.forbidden", "Attempt belongs to another user.");

        var revision = await revisions.GetAsync(attempt.RevisionId, ct);
        if (revision is null) return Error.NotFound("revision.not_found", "Published test revision was not found.");

        var score = revision.CalculateScore(attempt.Responses);
        var result = attempt.Submit(clock.UtcNow, score);
        var error = result.Match<Error?>(_ => null, e => e.ToApplicationError());
        if (error is not null) return error;

        await unitOfWork.SaveChangesAsync(ct);
        return score;
    }
}

public sealed class TimeoutAttemptCommandHandler(
    ITestAttemptRepository attempts,
    IClock clock,
    IUnitOfWork unitOfWork)
    : ICommandHandler<TimeoutAttemptCommand, Result<TestAttemptId, Error>>
{
    public async Task<Result<TestAttemptId, Error>> Handle(TimeoutAttemptCommand command, CancellationToken ct)
    {
        var attempt = await attempts.GetAsync(command.AttemptId, ct);
        if (attempt is null) return Error.NotFound("attempt.not_found", "Attempt was not found.");

        var result = attempt.Timeout(clock.UtcNow);
        var error = result.Match<Error?>(_ => null, e => e.ToApplicationError());
        if (error is not null) return error;

        await unitOfWork.SaveChangesAsync(ct);
        return attempt.Id;
    }
}
