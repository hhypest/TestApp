using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Attempts;

public sealed record StartAttemptCommand(TestAssignmentId AssignmentId) : ICommand<Result<TestAttemptId, Error>>;

public sealed class StartAttemptCommandHandler(
    ITestAssignmentRepository assignments,
    ITestAttemptRepository attempts,
    ICurrentActor actor,
    IClock clock,
    IUnitOfWork unitOfWork) : ICommandHandler<StartAttemptCommand, Result<TestAttemptId, Error>>
{
    public async Task<Result<TestAttemptId, Error>> Handle(StartAttemptCommand command, CancellationToken ct)
    {
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
            return Error.Conflict("assignment.unavailable", "The assignment is outside its availability window.");

        if (assignment.AttemptLimit is { } limit)
        {
            var count = await assignments.CountAttemptsAsync(assignment.Id, actor.UserId, ct);
            if (count >= limit)
                return Error.Conflict("assignment.attempt_limit_reached", "The attempt limit has been reached.");
        }

        var id = TestAttemptId.New();
        var attempt = TestAttempt.Start(id, assignment.Id, actor.UserId, now);
        await attempts.AddAsync(attempt, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return id;
    }
}
