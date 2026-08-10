using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Assignments;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Assignments;

public sealed record CancelAssignmentCommand(TestAssignmentId AssignmentId, string? Reason)
    : ICommand<Result<TestAssignmentId, Error>>;

public sealed record ChangeAssignmentWindowCommand(TestAssignmentId AssignmentId, DateTimeOffset AvailableFrom, DateTimeOffset? AvailableUntil)
    : ICommand<Result<TestAssignmentId, Error>>;

public sealed record ChangeAssignmentAttemptLimitCommand(TestAssignmentId AssignmentId, int? AttemptLimit)
    : ICommand<Result<TestAssignmentId, Error>>;

public sealed class CancelAssignmentCommandHandler(
    ITestAssignmentRepository assignments,
    ICurrentActor actor,
    IClock clock,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CancelAssignmentCommand, Result<TestAssignmentId, Error>>
{
    public async Task<Result<TestAssignmentId, Error>> Handle(CancelAssignmentCommand command, CancellationToken ct)
    {
        var assignment = await assignments.GetAsync(command.AssignmentId, ct);
        if (assignment is null) return Error.NotFound("assignment.not_found", "Test assignment was not found.");

        var result = assignment.Cancel(actor.UserId, clock.UtcNow, command.Reason);
        var error = result.Match<Error?>(_ => null, e => e.ToApplicationError());
        if (error is not null) return error;

        await unitOfWork.SaveChangesAsync(ct);
        return assignment.Id;
    }
}

public sealed class ChangeAssignmentWindowCommandHandler(
    ITestAssignmentRepository assignments,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ChangeAssignmentWindowCommand, Result<TestAssignmentId, Error>>
{
    public async Task<Result<TestAssignmentId, Error>> Handle(ChangeAssignmentWindowCommand command, CancellationToken ct)
    {
        var assignment = await assignments.GetAsync(command.AssignmentId, ct);
        if (assignment is null) return Error.NotFound("assignment.not_found", "Test assignment was not found.");

        var result = assignment.ChangeAvailability(command.AvailableFrom, command.AvailableUntil);
        var error = result.Match<Error?>(_ => null, e => e.ToApplicationError());
        if (error is not null) return error;

        await unitOfWork.SaveChangesAsync(ct);
        return assignment.Id;
    }
}

public sealed class ChangeAssignmentAttemptLimitCommandHandler(
    ITestAssignmentRepository assignments,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ChangeAssignmentAttemptLimitCommand, Result<TestAssignmentId, Error>>
{
    public async Task<Result<TestAssignmentId, Error>> Handle(ChangeAssignmentAttemptLimitCommand command, CancellationToken ct)
    {
        var assignment = await assignments.GetAsync(command.AssignmentId, ct);
        if (assignment is null) return Error.NotFound("assignment.not_found", "Test assignment was not found.");

        var result = assignment.ChangeAttemptLimit(command.AttemptLimit);
        var error = result.Match<Error?>(_ => null, e => e.ToApplicationError());
        if (error is not null) return error;

        await unitOfWork.SaveChangesAsync(ct);
        return assignment.Id;
    }
}
