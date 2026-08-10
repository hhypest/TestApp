using TestApp.Domain.Entities;

namespace TestApp.Domain.Tests;

public sealed class Test : AggregateRoot<TestId>
{
    private readonly List<Question> _questions = [];

    private Test(TestId id, string name) : base(id)
    {
        Name = name;
    }

    public string Name { get; private set; }
    public TestStatus Status { get; private set; } = TestStatus.Draft;
    public IReadOnlyCollection<Question> Questions => _questions.AsReadOnly();

    public static Test Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Test(TestId.New(), name.Trim());
    }

    public QuestionId AddQuestion(string text)
    {
        EnsureDraft();
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var question = new Question(QuestionId.New(), text.Trim());
        _questions.Add(question);
        return question.Id;
    }

    public void Publish(DateTimeOffset occurredAt)
    {
        EnsureDraft();

        if (_questions.Count == 0)
            throw new InvalidOperationException("A test without questions cannot be published.");

        Status = TestStatus.Published;
        Raise(new TestPublished(Id, occurredAt));
    }

    private void EnsureDraft()
    {
        if (Status != TestStatus.Draft)
            throw new InvalidOperationException("Only a draft test can be modified.");
    }
}

public sealed class Question : Entity<QuestionId>
{
    private readonly List<AnswerOption> _answerOptions = [];

    internal Question(QuestionId id, string text) : base(id)
    {
        Text = text;
    }

    public string Text { get; private set; }
    public IReadOnlyCollection<AnswerOption> AnswerOptions => _answerOptions.AsReadOnly();

    internal AnswerOptionId AddAnswerOption(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var answer = new AnswerOption(AnswerOptionId.New(), text.Trim());
        _answerOptions.Add(answer);
        return answer.Id;
    }
}

public sealed class AnswerOption : Entity<AnswerOptionId>
{
    internal AnswerOption(AnswerOptionId id, string text) : base(id)
    {
        Text = text;
    }

    public string Text { get; private set; }
}

public enum TestStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}
