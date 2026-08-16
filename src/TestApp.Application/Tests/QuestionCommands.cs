using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Tests;

public sealed record AddQuestionCommand(
    TestId TestId,
    string Text,
    QuestionType Type,
    decimal Points,
    int Order,
    long? ExpectedVersion = null) : ICommand<Result<QuestionId, Error>>;

public sealed record UpdateQuestionCommand(
    TestId TestId,
    QuestionId QuestionId,
    string Text,
    QuestionType Type,
    decimal Points,
    long? ExpectedVersion = null) : ICommand<Result<TestId, Error>>;

public sealed record RemoveQuestionCommand(
    TestId TestId,
    QuestionId QuestionId,
    long? ExpectedVersion = null) : ICommand<Result<TestId, Error>>;

public sealed record ReorderQuestionCommand(
    TestId TestId,
    QuestionId QuestionId,
    int Order,
    long? ExpectedVersion = null) : ICommand<Result<TestId, Error>>;

public sealed class AddQuestionCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<AddQuestionCommand, Result<QuestionId, Error>>
{
    public async Task<Result<QuestionId, Error>> Handle(AddQuestionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError) return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError) return versionError;

        var result = test.AddQuestion(command.Text, command.Type, command.Points, command.Order);
        return await result.Match(
            async id =>
            {
                await unitOfWork.SaveChangesAsync(ct);
                return Result<QuestionId, Error>.Success(id);
            },
            error => Task.FromResult(Result<QuestionId, Error>.Failure(error.ToApplicationError())));
    }
}

public sealed class UpdateQuestionCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateQuestionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(UpdateQuestionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError) return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError) return versionError;

        return await TestCommandResult.Save(
            test.UpdateQuestion(command.QuestionId, command.Text, command.Type, command.Points),
            test.Id,
            unitOfWork,
            ct);
    }
}

public sealed class RemoveQuestionCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<RemoveQuestionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(RemoveQuestionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError) return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError) return versionError;

        return await TestCommandResult.Save(test.RemoveQuestion(command.QuestionId), test.Id, unitOfWork, ct);
    }
}

public sealed class ReorderQuestionCommandHandler(ITestRepository tests, ICurrentActor actor, IUnitOfWork unitOfWork)
    : ICommandHandler<ReorderQuestionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(ReorderQuestionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        if (TestAccess.EnsureCanManage(test, actor) is { } accessError) return accessError;
        if (TestAccess.EnsureExpectedVersion(test, command.ExpectedVersion) is { } versionError) return versionError;

        return await TestCommandResult.Save(
            test.ReorderQuestion(command.QuestionId, command.Order),
            test.Id,
            unitOfWork,
            ct);
    }
}
