using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Tests;

public sealed record PublishTestCommand(TestId TestId) : ICommand<Result<PublishedTestRevisionId, Error>>;

public sealed class PublishTestCommandHandler(
    ITestRepository tests,
    IPublishedTestRevisionRepository revisions,
    IClock clock,
    IUnitOfWork unitOfWork) : ICommandHandler<PublishTestCommand, Result<PublishedTestRevisionId, Error>>
{
    public async Task<Result<PublishedTestRevisionId, Error>> Handle(PublishTestCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null)
            return Error.NotFound("test.not_found", "Test was not found.");
        if (test.Status != TestStatus.Draft)
            return Error.Conflict("test.not_draft", "Only a draft test can be published.");

        var now = clock.UtcNow;
        try
        {
            test.Publish(now);
        }
        catch (InvalidOperationException exception)
        {
            return Error.Validation("test.not_publishable", exception.Message);
        }

        var version = await revisions.GetNextVersionAsync(test.Id, ct);
        var revisionId = PublishedTestRevisionId.New();
        var revision = PublishedTestRevision.From(test, revisionId, version, now);
        await revisions.AddAsync(revision, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return revisionId;
    }
}
