using TestApp.Domain.Entities;

namespace TestApp.Domain.Tests;

public sealed class Test : AggregateRoot<TestId>
{
    private readonly List<Question> _questions = [];

    private Test() { Title = string.Empty; }
    private Test(TestId id, string title) : base(id) => Title = Normalize(title, nameof(title));

    public string Title { get; private set; }
    public TestStatus Status { get; private set; } = TestStatus.Draft;
    public IReadOnlyCollection<Question> Questions => _questions.AsReadOnly();

    public static Test Create(string title) => new(TestId.New(), title);

    public void Rename(string title)
    {
        EnsureDraft();
        Title = Normalize(title, nameof(title));
    }

    public QuestionId AddQuestion(string text, QuestionType type, decimal points, int order)
    {
        EnsureDraft();
        if (points <= 0) throw new ArgumentOutOfRangeException(nameof(points));
        if (order < 0) throw new ArgumentOutOfRangeException(nameof(order));
        if (_questions.Any(q => q.Order == order)) throw new InvalidOperationException("Question order must be unique within a test.");

        var question = new Question(QuestionId.New(), Normalize(text, nameof(text)), type, points, order);
        _questions.Add(question);
        return question.Id;
    }

    public AnswerOptionId AddAnswerOption(QuestionId questionId, string text, bool isCorrect, int order)
    {
        EnsureDraft();
        var question = _questions.SingleOrDefault(q => q.Id == questionId)
            ?? throw new InvalidOperationException("Question does not belong to this test.");
        return question.AddAnswerOption(text, isCorrect, order);
    }

    public void Publish(DateTimeOffset occurredAt)
    {
        EnsureDraft();
        if (_questions.Count == 0) throw new InvalidOperationException("A test without questions cannot be published.");
        if (_questions.Any(q => q.Options.Count == 0)) throw new InvalidOperationException("Every question must contain at least one answer option.");
        if (_questions.Any(q => !q.Options.Any(o => o.IsCorrect))) throw new InvalidOperationException("Every question must contain at least one correct answer option.");

        Status = TestStatus.Published;
        Raise(new TestPublished(Id, occurredAt));
    }

    public void Archive() => Status = TestStatus.Archived;

    private void EnsureDraft()
    {
        if (Status != TestStatus.Draft) throw new InvalidOperationException("Only a draft test can be modified.");
    }

    private static string Normalize(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        return value.Trim();
    }
}

public sealed class Question : Entity<QuestionId>
{
    private readonly List<AnswerOption> _options = [];

    private Question() { Text = string.Empty; }
    internal Question(QuestionId id, string text, QuestionType type, decimal points, int order) : base(id)
    {
        Text = text;
        Type = type;
        Points = points;
        Order = order;
    }

    public string Text { get; private set; }
    public QuestionType Type { get; private set; }
    public decimal Points { get; private set; }
    public int Order { get; private set; }
    public IReadOnlyCollection<AnswerOption> Options => _options.AsReadOnly();

    internal AnswerOptionId AddAnswerOption(string text, bool isCorrect, int order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (order < 0) throw new ArgumentOutOfRangeException(nameof(order));
        if (_options.Any(o => o.Order == order)) throw new InvalidOperationException("Answer option order must be unique within a question.");
        if (Type == QuestionType.SingleChoice && isCorrect && _options.Any(o => o.IsCorrect))
            throw new InvalidOperationException("A single-choice question can have only one correct option.");

        var option = new AnswerOption(AnswerOptionId.New(), text.Trim(), isCorrect, order);
        _options.Add(option);
        return option.Id;
    }
}

public sealed class AnswerOption : Entity<AnswerOptionId>
{
    private AnswerOption() { Text = string.Empty; }
    internal AnswerOption(AnswerOptionId id, string text, bool isCorrect, int order) : base(id)
    {
        Text = text;
        IsCorrect = isCorrect;
        Order = order;
    }

    public string Text { get; private set; }
    public bool IsCorrect { get; private set; }
    public int Order { get; private set; }
}

public enum QuestionType { SingleChoice = 1, MultipleChoice = 2 }
public enum TestStatus { Draft = 0, Published = 1, Archived = 2 }
