using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Attempts;

public sealed record StartAttemptCommand(TestAssignmentId AssignmentId, Guid StartRequestId) : ICommand<Result<TestAttemptId, Error>>;

public sealed class StartAttemptCommandHandler(
    ITestAssignmentRepository assignments,
    ITestAttemptRepository attempts,
    IPublishedTestRevisionRepository revisions,
    ICurrentActor actor,
    IClock clock) : ICommandHandler<StartAttemptCommand, Result<TestAttemptId, Error>>
{
    public async Task<Result<TestAttemptId, Error>> Handle(StartAttemptCommand command, CancellationToken ct)
    {
        if (command.StartRequestId == Guid.Empty)
            return Error.Validation("attempt.start_request_id", "A non-empty idempotency key is required.");

        var replayedId = await attempts.FindIdByStartRequestAsync(
            command.AssignmentId,
            actor.UserId,
            command.StartRequestId,
            ct);
        if (replayedId is not null)
            return replayedId.Value;

        var assignment = await assignments.GetAsync(command.AssignmentId, ct);
        if (assignment is null)
            return Error.NotFound("assignment.not_found", "Test assignment was not found.");

        var authorized = assignment.Target switch
        {
            AssignmentTarget.User user => user.UserId == actor.UserId,
            AssignmentTarget.Group group => actor.Groups.Contains(group.GroupId),
            _ => false
        };
        if (!authorized)
            return Error.Forbidden("assignment.forbidden", "The current user is not a target of this assignment.");

        var now = clock.UtcNow;
        if (!assignment.IsAvailableAt(now))
            return Error.Conflict("assignment.unavailable", "The assignment is unavailable or cancelled.");

        var revision = await revisions.GetAsync(assignment.RevisionId, ct);
        if (revision is null)
            return Error.NotFound("revision.not_found", "Published test revision was not found.");

        var attempt = TestAttempt.Start(
            TestAttemptId.New(),
            assignment.Id,
            revision.Id,
            actor.UserId,
            command.StartRequestId,
            now,
            revision.CalculateDeadline(now),
            revision.Questions.Select(q => q.Id));

        var persistedId = await attempts.TryAddWithinLimitAsync(attempt, assignment.AttemptLimit, ct);
        if (persistedId is null)
            return Error.Conflict("assignment.attempt_limit_reached", "The attempt limit has been reached.");

        return persistedId.Value;
    }
}
