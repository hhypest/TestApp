using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Assignments;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Assignments;

public sealed record AssignTestCommand(
    PublishedTestRevisionId RevisionId,
    ExternalUserId? UserId,
    ExternalGroupId? GroupId,
    DateTimeOffset AvailableFrom,
    DateTimeOffset? AvailableUntil,
    int? AttemptLimit) : ICommand<Result<TestAssignmentId, Error>>;

public sealed class AssignTestCommandHandler(
    ITestAssignmentRepository assignments,
    IPublishedTestRevisionRepository revisions,
    ICurrentActor actor,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<AssignTestCommand, Result<TestAssignmentId, Error>>
{
    public async Task<Result<TestAssignmentId, Error>> Handle(AssignTestCommand command, CancellationToken ct)
    {
        if ((command.UserId is null) == (command.GroupId is null))
            return Error.Validation("assignment.target", "Specify exactly one assignment target: user or group.");
        if (command.AvailableUntil is not null && command.AvailableUntil <= command.AvailableFrom)
            return Error.Validation("assignment.window", "AvailableUntil must be later than AvailableFrom.");
        if (command.AttemptLimit is <= 0)
            return Error.Validation("assignment.attempt_limit", "Attempt limit must be greater than zero.");

        var revision = await revisions.GetAsync(command.RevisionId, ct);
        if (revision is null)
            return Error.NotFound("revision.not_found", "Published test revision was not found.");

        AssignmentTarget target = command.UserId is { } user
            ? new AssignmentTarget.User(user)
            : new AssignmentTarget.Group(command.GroupId!.Value);

        var now = clock.UtcNow;
        var id = TestAssignmentId.New();
        var assignment = TestAssignment.Create(
            id,
            revision.Id,
            target,
            actor.UserId,
            now,
            command.AvailableFrom,
            command.AvailableUntil,
            command.AttemptLimit);

        await assignments.AddAsync(assignment, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return id;
    }
}
