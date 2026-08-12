using TestApp.Core.Monads;
using TestApp.Domain.Common;
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

    public Result<Test, DomainError> Rename(string title)
    {
        var editable = EnsureEditable();
        if (editable.Match(_ => false, _ => true))
            return editable.Match<Result<Test, DomainError>>(_ => this, error => error);

        Title = Normalize(title, nameof(title));
        MarkChanged();
        return this;
    }

    public Result<QuestionId, DomainError> AddQuestion(string text, QuestionType type, decimal points, int order)
    {
        var editable = EnsureEditable();
        if (editable.Match(_ => false, _ => true))
            return editable.Match<Result<QuestionId, DomainError>>(_ => QuestionId.New(), error => error);

        if (points <= 0)
            return DomainError.Validation("test.question.points", "Question points must be greater than zero.");
        if (order < 0)
            return DomainError.Validation("test.question.order", "Question order cannot be negative.");
        if (_questions.Any(q => q.Order == order))
            return DomainError.Conflict("test.question.order_duplicate", "Question order must be unique within a test.");

        var question = new Question(QuestionId.New(), Normalize(text, nameof(text)), type, points, order);
        _questions.Add(question);
        MarkChanged();
        return question.Id;
    }

    public Result<Test, DomainError> UpdateQuestion(QuestionId questionId, string text, QuestionType type, decimal points)
    {
        var editable = EnsureEditable();
        if (editable.Match(_ => false, _ => true))
            return editable.Match<Result<Test, DomainError>>(_ => this, error => error);

        if (points <= 0)
            return DomainError.Validation("test.question.points", "Question points must be greater than zero.");

        var question = FindQuestion(questionId);
        if (question is null)
            return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");

        question.Update(Normalize(text, nameof(text)), type, points);
        MarkChanged();
        return this;
    }

    public Result<Test, DomainError> RemoveQuestion(QuestionId questionId)
    {
        var editable = EnsureEditable();
        if (editable.Match(_ => false, _ => true))
            return editable.Match<Result<Test, DomainError>>(_ => this, error => error);

        var question = FindQuestion(questionId);
        if (question is null)
            return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");

        _questions.Remove(question);
        MarkChanged();
        return this;
    }

    public Result<Test, DomainError> ReorderQuestion(QuestionId questionId, int order)
    {
        var editable = EnsureEditable();
        if (editable.Match(_ => false, _ => true))
            return editable.Match<Result<Test, DomainError>>(_ => this, error => error);

        if (order < 0)
            return DomainError.Validation("test.question.order", "Question order cannot be negative.");
        if (_questions.Any(q => q.Id != questionId && q.Order == order))
            return DomainError.Conflict("test.question.order_duplicate", "Question order must be unique within a test.");

        var question = FindQuestion(questionId);
        if (question is null)
            return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");

        question.SetOrder(order);
        MarkChanged();
        return this;
    }

    public Result<AnswerOptionId, DomainError> AddAnswerOption(QuestionId questionId, string text, bool isCorrect, int order)
    {
        var editable = EnsureEditable();
        if (editable.Match(_ => false, _ => true))
            return editable.Match<Result<AnswerOptionId, DomainError>>(_ => AnswerOptionId.New(), error => error);

        var question = FindQuestion(questionId);
        if (question is null)
            return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");

        var result = question.AddAnswerOption(text, isCorrect, order);
        return result.Match<Result<AnswerOptionId, DomainError>>(
            id =>
            {
                MarkChanged();
                return id;
            },
            error => error);
    }

    public Result<Test, DomainError> UpdateAnswerOption(QuestionId questionId, AnswerOptionId optionId, string text, bool isCorrect)
    {
        var editable = EnsureEditable();
        if (editable.Match(_ => false, _ => true))
            return editable.Match<Result<Test, DomainError>>(_ => this, error => error);

        var question = FindQuestion(questionId);
        if (question is null)
            return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");

        var result = question.UpdateAnswerOption(optionId, text, isCorrect);
        return result.Match<Result<Test, DomainError>>(
            _ =>
            {
                MarkChanged();
                return this;
            },
            error => error);
    }

    public Result<Test, DomainError> RemoveAnswerOption(QuestionId questionId, AnswerOptionId optionId)
    {
        var editable = EnsureEditable();
        if (editable.Match(_ => false, _ => true))
            return editable.Match<Result<Test, DomainError>>(_ => this, error => error);

        var question = FindQuestion(questionId);
        if (question is null)
            return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");

        var result = question.RemoveAnswerOption(optionId);
        return result.Match<Result<Test, DomainError>>(
            _ =>
            {
                MarkChanged();
                return this;
            },
            error => error);
    }

    public Result<Test, DomainError> ReorderAnswerOption(QuestionId questionId, AnswerOptionId optionId, int order)
    {
        var editable = EnsureEditable();
        if (editable.Match(_ => false, _ => true))
            return editable.Match<Result<Test, DomainError>>(_ => this, error => error);

        var question = FindQuestion(questionId);
        if (question is null)
            return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");

        var result = question.ReorderAnswerOption(optionId, order);
        return result.Match<Result<Test, DomainError>>(
            _ =>
            {
                MarkChanged();
                return this;
            },
            error => error);
    }

    public Result<Test, DomainError> Publish(DateTimeOffset occurredAt)
    {
        var editable = EnsureEditable();
        if (editable.Match(_ => false, _ => true))
            return editable.Match<Result<Test, DomainError>>(_ => this, error => error);

        if (_questions.Count == 0)
            return DomainError.Validation("test.publish.questions_required", "A test without questions cannot be published.");

        foreach (var question in _questions)
        {
            var validation = question.ValidateForPublication();
            var failed = validation.Match(_ => false, _ => true);
            if (failed)
                return validation.Match<Result<Test, DomainError>>(_ => this, error => error);
        }

        Status = TestStatus.Published;
        Touch();
        Raise(new TestPublished(Id, occurredAt));
        return this;
    }

    public Result<Test, DomainError> Archive()
    {
        if (Status == TestStatus.Archived)
            return DomainError.Conflict("test.archived", "Test is already archived.");

        Status = TestStatus.Archived;
        Touch();
        return this;
    }

    private Result<Test, DomainError> EnsureEditable()
        => Status == TestStatus.Archived
            ? DomainError.Conflict("test.archived", "Archived tests cannot be modified.")
            : this;

    private Question? FindQuestion(QuestionId questionId) => _questions.SingleOrDefault(q => q.Id == questionId);

    private void MarkChanged()
    {
        if (Status == TestStatus.Published)
            Status = TestStatus.Draft;
        Touch();
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

    internal void Update(string text, QuestionType type, decimal points)
    {
        Text = text;
        Type = type;
        Points = points;
    }

    internal void SetOrder(int order) => Order = order;

    internal Result<AnswerOptionId, DomainError> AddAnswerOption(string text, bool isCorrect, int order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (order < 0)
            return DomainError.Validation("test.answer_option.order", "Answer option order cannot be negative.");
        if (_options.Any(o => o.Order == order))
            return DomainError.Conflict("test.answer_option.order_duplicate", "Answer option order must be unique within a question.");
        if (_options.Any(o => string.Equals(o.Text, text.Trim(), StringComparison.OrdinalIgnoreCase)))
            return DomainError.Conflict("test.answer_option.text_duplicate", "Answer option text must be unique within a question.");
        if (Type == QuestionType.SingleChoice && isCorrect && _options.Any(o => o.IsCorrect))
            return DomainError.Conflict("test.single_choice.multiple_correct", "A single-choice question can have only one correct option.");

        var option = new AnswerOption(AnswerOptionId.New(), text.Trim(), isCorrect, order);
        _options.Add(option);
        return option.Id;
    }

    internal Result<Question, DomainError> UpdateAnswerOption(AnswerOptionId optionId, string text, bool isCorrect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var option = _options.SingleOrDefault(o => o.Id == optionId);
        if (option is null)
            return DomainError.NotFound("test.answer_option.not_found", "Answer option was not found in this question.");
        if (_options.Any(o => o.Id != optionId && string.Equals(o.Text, text.Trim(), StringComparison.OrdinalIgnoreCase)))
            return DomainError.Conflict("test.answer_option.text_duplicate", "Answer option text must be unique within a question.");
        if (Type == QuestionType.SingleChoice && isCorrect && _options.Any(o => o.Id != optionId && o.IsCorrect))
            return DomainError.Conflict("test.single_choice.multiple_correct", "A single-choice question can have only one correct option.");

        option.Update(text.Trim(), isCorrect);
        return this;
    }

    internal Result<Question, DomainError> RemoveAnswerOption(AnswerOptionId optionId)
    {
        var option = _options.SingleOrDefault(o => o.Id == optionId);
        if (option is null)
            return DomainError.NotFound("test.answer_option.not_found", "Answer option was not found in this question.");

        _options.Remove(option);
        return this;
    }

    internal Result<Question, DomainError> ReorderAnswerOption(AnswerOptionId optionId, int order)
    {
        if (order < 0)
            return DomainError.Validation("test.answer_option.order", "Answer option order cannot be negative.");
        if (_options.Any(o => o.Id != optionId && o.Order == order))
            return DomainError.Conflict("test.answer_option.order_duplicate", "Answer option order must be unique within a question.");

        var option = _options.SingleOrDefault(o => o.Id == optionId);
        if (option is null)
            return DomainError.NotFound("test.answer_option.not_found", "Answer option was not found in this question.");

        option.SetOrder(order);
        return this;
    }

    internal Result<Question, DomainError> ValidateForPublication()
    {
        if (_options.Count < 2)
            return DomainError.Validation("test.question.options_required", "Choice questions must contain at least two answer options.");

        var correctCount = _options.Count(o => o.IsCorrect);
        if (Type == QuestionType.SingleChoice && correctCount != 1)
            return DomainError.Validation("test.single_choice.correct_count", "A single-choice question must contain exactly one correct answer option.");
        if (Type == QuestionType.MultipleChoice && correctCount < 1)
            return DomainError.Validation("test.multiple_choice.correct_required", "A multiple-choice question must contain at least one correct answer option.");

        return this;
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

    internal void Update(string text, bool isCorrect)
    {
        Text = text;
        IsCorrect = isCorrect;
    }

    internal void SetOrder(int order) => Order = order;
}

public enum QuestionType { SingleChoice = 1, MultipleChoice = 2 }
public enum TestStatus { Draft = 0, Published = 1, Archived = 2 }
