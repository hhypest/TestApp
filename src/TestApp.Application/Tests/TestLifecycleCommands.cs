using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Tests;

public sealed record CreateTestCommand(string Title) : ICommand<Result<TestId, Error>>;

public sealed record RenameTestCommand(
    TestId TestId,
    string Title,
    long? ExpectedVersion = null) : ICommand<Result<TestId, Error>>;

public sealed record ChangeTestSettingsCommand(
    TestId TestId,
    decimal PassingPercentage,
    int? TimeLimitMinutes,
    long? ExpectedVersion = null) : ICommand<Result<TestId, Error>>;

public sealed record ArchiveTestCommand(
    TestId TestId,
    long? ExpectedVersion = null) : ICommand<Result<TestId, Error>>;

public sealed class CreateTestCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<CreateTestCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(CreateTestCommand command, CancellationToken ct)
    {
        var created = Test.Create(command.Title, actor.UserId);
        return await created.Match(
            async test =>
            {
                await tests.AddAsync(test, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return Result<TestId, Error>.Success(test.Id);
            },
            error => Task.FromResult(Result<TestId, Error>.Failure(error.ToApplicationError())));
    }
}

public sealed class RenameTestCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<RenameTestCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(RenameTestCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError) return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError) return versionError;

        return await TestCommandResult.Save(test.Rename(command.Title), test.Id, unitOfWork, ct);
    }
}

public sealed class ChangeTestSettingsCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<ChangeTestSettingsCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(ChangeTestSettingsCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError) return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError) return versionError;

        return await TestCommandResult.Save(
            test.ChangeSettings(command.PassingPercentage, command.TimeLimitMinutes),
            test.Id,
            unitOfWork,
            ct);
    }
}

public sealed class ArchiveTestCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<ArchiveTestCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(ArchiveTestCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError) return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError) return versionError;

        return await TestCommandResult.Save(test.Archive(), test.Id, unitOfWork, ct);
    }
}

internal static class TestCommandResult
{
    public static async Task<Result<TestId, Error>> Save(
        Result<Test, TestApp.Domain.Common.DomainError> result,
        TestId id,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
        => await result.Match(
            async _ =>
            {
                await unitOfWork.SaveChangesAsync(ct);
                return Result<TestId, Error>.Success(id);
            },
            error => Task.FromResult(Result<TestId, Error>.Failure(error.ToApplicationError())));
}
