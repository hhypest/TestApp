namespace TestApp.Domain.Tests;

public readonly record struct TestId(Guid Value)
{
    public static TestId New() => new(Guid.CreateVersion7());
}

public readonly record struct QuestionId(Guid Value)
{
    public static QuestionId New() => new(Guid.CreateVersion7());
}

public readonly record struct AnswerOptionId(Guid Value)
{
    public static AnswerOptionId New() => new(Guid.CreateVersion7());
}
