using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Assignments;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Assignments;

public sealed record BulkAssignmentTarget(ExternalUserId? UserId, ExternalGroupId? GroupId);

public readonly record struct BulkAssignTestsResult(int CreatedCount, TestAssignmentId[] AssignmentIds);

public sealed record BulkAssignTestsCommand(
    PublishedTestRevisionId RevisionId,
    IReadOnlyCollection<BulkAssignmentTarget> Targets,
    DateTimeOffset AvailableFrom,
    DateTimeOffset? AvailableUntil,
    int? AttemptLimit,
    Guid RequestId) : ICommand<Result<BulkAssignTestsResult, Error>>;

public sealed class BulkAssignTestsCommandHandler(
    ITestAssignmentRepository assignments,
    IPublishedTestRevisionRepository revisions,
    ICurrentActor actor,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<BulkAssignTestsCommand, Result<BulkAssignTestsResult, Error>>
{
    private const string Operation = "assignments.bulk-create";
    private const int MaxBatchSize = 500;

    public async Task<Result<BulkAssignTestsResult, Error>> Handle(BulkAssignTestsCommand command, CancellationToken ct)
    {
        if (command.RequestId == Guid.Empty)
            return Error.Validation("idempotency.request_id", "A non-empty idempotency key is required.");

        var canonicalTargets = command.Targets
            .Select(target => $"u:{target.UserId?.Value ?? string.Empty}|g:{target.GroupId?.Value ?? string.Empty}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var fingerprint = IdempotencyFingerprint.Create(
            IdempotencyFingerprint.Guid(command.RevisionId.Value),
            string.Join("\n", canonicalTargets),
            IdempotencyFingerprint.Instant(command.AvailableFrom),
            IdempotencyFingerprint.OptionalInstant(command.AvailableUntil),
            IdempotencyFingerprint.OptionalInt(command.AttemptLimit));

        var cached = await idempotency.GetResultAsync<BulkAssignTestsResult>(Operation, actor.UserId, command.RequestId, fingerprint, ct);
        if (cached is { } cachedResult)
            return cachedResult;

        await using var lease = await idempotency.AcquireAsync(Operation, actor.UserId, command.RequestId, ct);

        cached = await idempotency.GetResultAsync<BulkAssignTestsResult>(Operation, actor.UserId, command.RequestId, fingerprint, ct);
        if (cached is { } leasedCachedResult)
            return leasedCachedResult;

        if (command.Targets.Count == 0)
            return Error.Validation("assignment.targets_required", "At least one assignment target is required.");
        if (command.Targets.Count > MaxBatchSize)
            return Error.Validation("assignment.targets_limit", $"A bulk assignment cannot contain more than {MaxBatchSize} targets.");

        var revision = await revisions.GetAsync(command.RevisionId, ct);
        if (revision is null)
            return Error.NotFound("revision.not_found", "Published test revision was not found.");

        var domainTargets = new List<AssignmentTarget>(command.Targets.Count);
        var deduplication = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in command.Targets)
        {
            if ((target.UserId is null) == (target.GroupId is null))
                return Error.Validation("assignment.target", "Each target must specify exactly one user or group.");

            AssignmentTarget domainTarget;
            string key;
            if (target.UserId is { } userId)
            {
                domainTarget = new AssignmentTarget.User(userId);
                key = $"user:{userId.Value}";
            }
            else
            {
                var groupId = target.GroupId!.Value;
                domainTarget = new AssignmentTarget.Group(groupId);
                key = $"group:{groupId.Value}";
            }

            if (!deduplication.Add(key))
                return Error.Validation("assignment.target_duplicate", $"Duplicate assignment target '{key}'.");

            domainTargets.Add(domainTarget);
        }

        var now = clock.UtcNow;
        var ids = new TestAssignmentId[domainTargets.Count];
        var created = new List<TestAssignment>(domainTargets.Count);
        for (var i = 0; i < domainTargets.Count; i++)
        {
            var id = TestAssignmentId.New();
            ids[i] = id;
            var assignment = TestAssignment.Create(
                id,
                revision.Id,
                domainTargets[i],
                actor.UserId,
                now,
                command.AvailableFrom,
                command.AvailableUntil,
                command.AttemptLimit);
            if (assignment.TryGetError(out var creationError)) return creationError.ToApplicationError();
            assignment.Tap(created.Add);
        }

        foreach (var assignment in created)
            await assignments.AddAsync(assignment, ct);

        var result = new BulkAssignTestsResult(ids.Length, ids);
        await idempotency.AddResultAsync(Operation, actor.UserId, command.RequestId, fingerprint, result, now, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return result;
    }
}
