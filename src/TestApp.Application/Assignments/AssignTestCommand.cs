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
    int? AttemptLimit,
    Guid RequestId) : ICommand<Result<TestAssignmentId, Error>>;

public sealed class AssignTestCommandHandler(
    ITestAssignmentRepository assignments,
    IPublishedTestRevisionRepository revisions,
    ICurrentActor actor,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<AssignTestCommand, Result<TestAssignmentId, Error>>
{
    private const string Operation = "assignments.create";

    public async Task<Result<TestAssignmentId, Error>> Handle(AssignTestCommand command, CancellationToken ct)
    {
        if (command.RequestId == Guid.Empty)
            return Error.Validation("idempotency.request_id", "A non-empty idempotency key is required.");

        var fingerprint = IdempotencyFingerprint.Create(
            IdempotencyFingerprint.Guid(command.RevisionId.Value),
            command.UserId?.Value,
            command.GroupId?.Value,
            IdempotencyFingerprint.Instant(command.AvailableFrom),
            IdempotencyFingerprint.OptionalInstant(command.AvailableUntil),
            IdempotencyFingerprint.OptionalInt(command.AttemptLimit));

        var cached = await idempotency.GetResultAsync<TestAssignmentId>(Operation, actor.UserId, command.RequestId, fingerprint, ct);
        if (cached is { } cachedAssignmentId)
            return cachedAssignmentId;

        await using var lease = await idempotency.AcquireAsync(Operation, actor.UserId, command.RequestId, ct);

        cached = await idempotency.GetResultAsync<TestAssignmentId>(Operation, actor.UserId, command.RequestId, fingerprint, ct);
        if (cached is { } leasedCachedAssignmentId)
            return leasedCachedAssignmentId;

        if ((command.UserId is null) == (command.GroupId is null))
            return Error.Validation("assignment.target", "Specify exactly one assignment target: user or group.");

        var revision = await revisions.GetAsync(command.RevisionId, ct);
        if (revision is null)
            return Error.NotFound("revision.not_found", "Published test revision was not found.");

        AssignmentTarget target = command.UserId is { } user
            ? new AssignmentTarget.User(user)
            : new AssignmentTarget.Group(command.GroupId!.Value);

        var now = clock.UtcNow;
        var id = TestAssignmentId.New();
        var created = TestAssignment.Create(id, revision.Id, target, actor.UserId, now, command.AvailableFrom, command.AvailableUntil, command.AttemptLimit);
        return await created.Match(
            async assignment =>
            {
                await assignments.AddAsync(assignment, ct);
                await idempotency.AddResultAsync(Operation, actor.UserId, command.RequestId, fingerprint, id, now, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return Result<TestAssignmentId, Error>.Success(id);
            },
            error => Task.FromResult(Result<TestAssignmentId, Error>.Failure(error.ToApplicationError())));
    }
}
