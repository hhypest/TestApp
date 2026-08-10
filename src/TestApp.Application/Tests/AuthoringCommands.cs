using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Tests;

public sealed record CreateTestCommand(string Title) : ICommand<Result<TestId, Error>>;
public sealed record RenameTestCommand(TestId TestId, string Title) : ICommand<Result<TestId, Error>>;
public sealed record AddQuestionCommand(TestId TestId, string Text, QuestionType Type, decimal Points, int Order) : ICommand<Result<QuestionId, Error>>;
public sealed record UpdateQuestionCommand(TestId TestId, QuestionId QuestionId, string Text, QuestionType Type, decimal Points) : ICommand<Result<TestId, Error>>;
public sealed record RemoveQuestionCommand(TestId TestId, QuestionId QuestionId) : ICommand<Result<TestId, Error>>;
public sealed record ReorderQuestionCommand(TestId TestId, QuestionId QuestionId, int Order) : ICommand<Result<TestId, Error>>;
public sealed record AddAnswerOptionCommand(TestId TestId, QuestionId QuestionId, string Text, bool IsCorrect, int Order) : ICommand<Result<AnswerOptionId, Error>>;
public sealed record UpdateAnswerOptionCommand(TestId TestId, QuestionId QuestionId, AnswerOptionId OptionId, string Text, bool IsCorrect) : ICommand<Result<TestId, Error>>;
public sealed record RemoveAnswerOptionCommand(TestId TestId, QuestionId QuestionId, AnswerOptionId OptionId) : ICommand<Result<TestId, Error>>;
public sealed record ReorderAnswerOptionCommand(TestId TestId, QuestionId QuestionId, AnswerOptionId OptionId, int Order) : ICommand<Result<TestId, Error>>;
public sealed record ArchiveTestCommand(TestId TestId) : ICommand<Result<TestId, Error>>;

public sealed class CreateTestCommandHandler(ITestRepository tests, IUnitOfWork unitOfWork)
    : ICommandHandler<CreateTestCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(CreateTestCommand command, CancellationToken ct)
    {
        try
        {
            var test = Test.Create(command.Title);
            await tests.AddAsync(test, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return test.Id;
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("test.title", ex.Message);
        }
    }
}

public sealed class RenameTestCommandHandler(ITestRepository tests, IUnitOfWork unitOfWork)
    : ICommandHandler<RenameTestCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(RenameTestCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        try { return await TestCommandResult.Save(test.Rename(command.Title), test.Id, unitOfWork, ct); }
        catch (ArgumentException ex) { return Error.Validation("test.title", ex.Message); }
    }
}

public sealed class AddQuestionCommandHandler(ITestRepository tests, IUnitOfWork unitOfWork)
    : ICommandHandler<AddQuestionCommand, Result<QuestionId, Error>>
{
    public async Task<Result<QuestionId, Error>> Handle(AddQuestionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        try
        {
            var result = test.AddQuestion(command.Text, command.Type, command.Points, command.Order);
            return await result.Match(
                async id => { await unitOfWork.SaveChangesAsync(ct); return Result<QuestionId, Error>.Success(id); },
                error => Task.FromResult(Result<QuestionId, Error>.Failure(error.ToApplicationError())));
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("test.question.text", ex.Message);
        }
    }
}

public sealed class UpdateQuestionCommandHandler(ITestRepository tests, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateQuestionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(UpdateQuestionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        try { return await TestCommandResult.Save(test.UpdateQuestion(command.QuestionId, command.Text, command.Type, command.Points), test.Id, unitOfWork, ct); }
        catch (ArgumentException ex) { return Error.Validation("test.question.text", ex.Message); }
    }
}

public sealed class RemoveQuestionCommandHandler(ITestRepository tests, IUnitOfWork unitOfWork)
    : ICommandHandler<RemoveQuestionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(RemoveQuestionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        return await TestCommandResult.Save(test.RemoveQuestion(command.QuestionId), test.Id, unitOfWork, ct);
    }
}

public sealed class ReorderQuestionCommandHandler(ITestRepository tests, IUnitOfWork unitOfWork)
    : ICommandHandler<ReorderQuestionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(ReorderQuestionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        return await TestCommandResult.Save(test.ReorderQuestion(command.QuestionId, command.Order), test.Id, unitOfWork, ct);
    }
}

public sealed class AddAnswerOptionCommandHandler(ITestRepository tests, IUnitOfWork unitOfWork)
    : ICommandHandler<AddAnswerOptionCommand, Result<AnswerOptionId, Error>>
{
    public async Task<Result<AnswerOptionId, Error>> Handle(AddAnswerOptionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        try
        {
            var result = test.AddAnswerOption(command.QuestionId, command.Text, command.IsCorrect, command.Order);
            return await result.Match(
                async id => { await unitOfWork.SaveChangesAsync(ct); return Result<AnswerOptionId, Error>.Success(id); },
                error => Task.FromResult(Result<AnswerOptionId, Error>.Failure(error.ToApplicationError())));
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("test.answer_option.text", ex.Message);
        }
    }
}

public sealed class UpdateAnswerOptionCommandHandler(ITestRepository tests, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateAnswerOptionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(UpdateAnswerOptionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        try { return await TestCommandResult.Save(test.UpdateAnswerOption(command.QuestionId, command.OptionId, command.Text, command.IsCorrect), test.Id, unitOfWork, ct); }
        catch (ArgumentException ex) { return Error.Validation("test.answer_option.text", ex.Message); }
    }
}

public sealed class RemoveAnswerOptionCommandHandler(ITestRepository tests, IUnitOfWork unitOfWork)
    : ICommandHandler<RemoveAnswerOptionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(RemoveAnswerOptionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        return await TestCommandResult.Save(test.RemoveAnswerOption(command.QuestionId, command.OptionId), test.Id, unitOfWork, ct);
    }
}

public sealed class ReorderAnswerOptionCommandHandler(ITestRepository tests, IUnitOfWork unitOfWork)
    : ICommandHandler<ReorderAnswerOptionCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(ReorderAnswerOptionCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
        return await TestCommandResult.Save(test.ReorderAnswerOption(command.QuestionId, command.OptionId, command.Order), test.Id, unitOfWork, ct);
    }
}

public sealed class ArchiveTestCommandHandler(ITestRepository tests, IUnitOfWork unitOfWork)
    : ICommandHandler<ArchiveTestCommand, Result<TestId, Error>>
{
    public async Task<Result<TestId, Error>> Handle(ArchiveTestCommand command, CancellationToken ct)
    {
        var test = await tests.GetAsync(command.TestId, ct);
        if (test is null) return Error.NotFound("test.not_found", "Test was not found.");
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
            async _ => { await unitOfWork.SaveChangesAsync(ct); return Result<TestId, Error>.Success(id); },
            error => Task.FromResult(Result<TestId, Error>.Failure(error.ToApplicationError())));
}
