using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Tests;

public sealed record AddAnswerOptionCommand(
    TestId TestId,
    QuestionId QuestionId,
    string Text,
    bool IsCorrect,
    int Order,
    long? ExpectedVersion = null) : ICommand<Result<AnswerOptionId, Error>>;

public sealed record UpdateAnswerOptionCommand(
    TestId TestId,
    QuestionId QuestionId,
    AnswerOptionId OptionId,
    string Text,
    bool IsCorrect,
    long? ExpectedVersion = null) : ICommand<Result<TestId, Error>>;

public sealed record RemoveAnswerOptionCommand(
    TestId TestId,
    QuestionId QuestionId,
    AnswerOptionId OptionId,
    long? ExpectedVersion = null) : ICommand<Result<TestId, Error>>;

public sealed record ReorderAnswerOptionCommand(
    TestId TestId,
    QuestionId QuestionId,
    AnswerOptionId OptionId,
    int Order,
    long? ExpectedVersion = null) : ICommand<Result<TestId, Error>>;

public sealed class AddAnswerOptionCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<AddAnswerOptionCommand, Result<AnswerOptionId, Error>>
{
    public async Task<Result<AnswerOptionId, Error>> Handle(AddAnswerOptionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError) return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError) return versionError;

        var result = test.AddAnswerOption(
            command.QuestionId,
            command.Text,
            command.IsCorrect,
            command.Order);
        return await result.Match(
            async id =>
            {
                await unitOfWork.SaveChangesAsync(ct);
                return Result<AnswerOptionId, Error>.Success(id);
            },
            error => Task.FromResult(Result<AnswerOptionId, Error>.Failure(error.ToApplicationError())));
    }
}

public sealed class UpdateAnswerOptionCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateAnswerOptionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(UpdateAnswerOptionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError) return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError) return versionError;

        return await TestCommandResult.Save(
            test.UpdateAnswerOption(command.QuestionId, command.OptionId, command.Text, command.IsCorrect),
            test.Id,
            unitOfWork,
            ct);
    }
}

public sealed class RemoveAnswerOptionCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<RemoveAnswerOptionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(RemoveAnswerOptionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError) return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError) return versionError;

        return await TestCommandResult.Save(
            test.RemoveAnswerOption(command.QuestionId, command.OptionId),
            test.Id,
            unitOfWork,
            ct);
    }
}

public sealed class ReorderAnswerOptionCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<ReorderAnswerOptionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(ReorderAnswerOptionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError) return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError) return versionError;

        return await TestCommandResult.Save(
            test.ReorderAnswerOption(command.QuestionId, command.OptionId, command.Order),
            test.Id,
            unitOfWork,
            ct);
    }
}
