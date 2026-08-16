using System.Text.Json.Serialization;
using TestApp.Domain.Common;

namespace TestApp.Domain.Tests;

public readonly record struct TestId
{
    [JsonConstructor]
    public TestId(Guid value) => Value = StrongIdGuard.Ensure(value, "Test id", nameof(value));

    public Guid Value { get; }

    public static TestId New() => new(Guid.CreateVersion7());
}

public readonly record struct QuestionId
{
    [JsonConstructor]
    public QuestionId(Guid value) => Value = StrongIdGuard.Ensure(value, "Question id", nameof(value));

    public Guid Value { get; }

    public static QuestionId New() => new(Guid.CreateVersion7());
}

public readonly record struct AnswerOptionId
{
    [JsonConstructor]
    public AnswerOptionId(Guid value) => Value = StrongIdGuard.Ensure(value, "Answer option id", nameof(value));

    public Guid Value { get; }

    public static AnswerOptionId New() => new(Guid.CreateVersion7());
}
