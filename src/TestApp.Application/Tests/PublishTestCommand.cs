using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Tests;

public sealed record PublishTestCommand(TestId TestId, Guid RequestId, long? ExpectedVersion = null) : ICommand<Result<PublishedTestRevisionId, Error>>;

public sealed class PublishTestCommandHandler(
    ITestRepository tests,
    IPublishedTestRevisionRepository revisions,
    ICurrentActor actor,
    IIdempotencyStore idempotency,
    IClock clock,
    IUnitOfWork unitOfWork) : ICommandHandler<PublishTestCommand, Result<PublishedTestRevisionId, Error>>
{
    public async Task<Result<PublishedTestRevisionId, Error>> Handle(PublishTestCommand command, CancellationToken ct)
    {
        if (command.RequestId == Guid.Empty)
            return Error.Validation("idempotency.request_id", "A non-empty idempotency key is required.");

        var operation = $"tests.publish:{command.TestId.Value:N}";
        var fingerprint = IdempotencyFingerprint.Create(IdempotencyFingerprint.Guid(command.TestId.Value));
        var cached = await idempotency.GetResultAsync<PublishedTestRevisionId>(operation, actor.UserId, command.RequestId, fingerprint, ct);
        if (cached is { } cachedRevisionId)
            return cachedRevisionId;

        await using var lease = await idempotency.AcquireAsync(operation, actor.UserId, command.RequestId, ct);

        cached = await idempotency.GetResultAsync<PublishedTestRevisionId>(operation, actor.UserId, command.RequestId, fingerprint, ct);
        if (cached is { } leasedCachedRevisionId)
            return leasedCachedRevisionId;

        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null)
            return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError)
            return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError)
            return versionError;
        if (test.Status == TestStatus.Archived)
            return Error.Conflict("test.archived", "Archived tests cannot be published.");
        if (test.Status == TestStatus.Published)
            return Error.Conflict("test.no_changes", "The current test definition is already published.");

        var now = clock.UtcNow;
        var publish = test.Publish(now);
        var publishError = publish.Match<Error?>(_ => null, error => error.ToApplicationError());
        if (publishError is not null)
            return publishError;

        var version = await revisions.GetNextVersionAsync(test.Id, ct);
        var revisionId = PublishedTestRevisionId.New();
        var revision = PublishedTestRevision.From(test, revisionId, version, now);
        await revisions.AddAsync(revision, ct);
        await idempotency.AddResultAsync(operation, actor.UserId, command.RequestId, fingerprint, revisionId, now, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return revisionId;
    }
}
