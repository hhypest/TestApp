using TestApp.Core.Monads;
using TestApp.Domain.Common;
using TestApp.Domain.Entities;
using TestApp.Domain.Identity;

namespace TestApp.Domain.Tests;

public static class TestLimits
{
    public const int TitleMaxLength = 300;
    public const int QuestionTextMaxLength = 2000;
    public const int AnswerOptionTextMaxLength = 2000;
}

public sealed class Test : AggregateRoot<TestId>
{
    private readonly List<Question> _questions = [];

    private Test() { Title = string.Empty; Settings = TestSettings.Default(); }
    private Test(TestId id, string normalizedTitle, ExternalUserId ownerId) : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId.Value, nameof(ownerId));
        if (ownerId.Value.Length > ExternalIdentityLimits.MaxIdentifierLength)
            throw new ArgumentException($"Owner identifier cannot exceed {ExternalIdentityLimits.MaxIdentifierLength} characters.", nameof(ownerId));
        Title = normalizedTitle;
        OwnerId = ownerId;
        Settings = TestSettings.Default();
    }

    public string Title { get; private set; }
    public ExternalUserId OwnerId { get; private set; }
    public TestStatus Status { get; private set; } = TestStatus.Draft;
    public TestSettings Settings { get; private set; }
    public IReadOnlyCollection<Question> Questions => _questions.AsReadOnly();

    public static Result<Test, DomainError> Create(string title, ExternalUserId ownerId) =>
        NormalizeTitle(title).Match<Result<Test, DomainError>>(
            normalizedTitle => new Test(TestId.New(), normalizedTitle, ownerId),
            error => error);

    public bool IsOwnedBy(ExternalUserId userId) => OwnerId == userId;

    public Result<Test, DomainError> Rename(string title)
    {
        var editable = EnsureEditable();
        if (editable.TryGetError(out var error)) return error;
        return NormalizeTitle(title).Match<Result<Test, DomainError>>(
            normalizedTitle => { Title = normalizedTitle; MarkChanged(); return this; },
            titleError => titleError);
    }

    public Result<Test, DomainError> ChangeSettings(decimal passingPercentage, int? timeLimitMinutes)
    {
        var editable = EnsureEditable();
        if (editable.TryGetError(out var error)) return error;
        var settings = TestSettings.Create(passingPercentage, timeLimitMinutes);
        return settings.Match<Result<Test, DomainError>>(
            value => { Settings = value; MarkChanged(); return this; },
            error => error);
    }

    public Result<QuestionId, DomainError> AddQuestion(string text, QuestionType type, decimal points, int order)
    {
        var editable = EnsureEditable();
        if (editable.TryGetError(out var error)) return error;
        if (!Enum.IsDefined(typeof(QuestionType), type)) return DomainError.Validation("test.question.type", "Question type is not supported.");
        if (points <= 0) return DomainError.Validation("test.question.points", "Question points must be greater than zero.");
        if (order < 0) return DomainError.Validation("test.question.order", "Question order cannot be negative.");
        if (_questions.Any(q => q.Order == order)) return DomainError.Conflict("test.question.order_duplicate", "Question order must be unique within a test.");
        return NormalizeQuestionText(text).Match<Result<QuestionId, DomainError>>(
            normalizedText =>
            {
                var question = new Question(QuestionId.New(), normalizedText, type, points, order);
                _questions.Add(question);
                MarkChanged();
                return question.Id;
            },
            error => error);
    }

    public Result<Test, DomainError> UpdateQuestion(QuestionId questionId, string text, QuestionType type, decimal points)
    {
        var editable = EnsureEditable();
        if (editable.TryGetError(out var error)) return error;
        if (!Enum.IsDefined(typeof(QuestionType), type)) return DomainError.Validation("test.question.type", "Question type is not supported.");
        if (points <= 0) return DomainError.Validation("test.question.points", "Question points must be greater than zero.");
        var question = FindQuestion(questionId); if (question is null) return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");
        return NormalizeQuestionText(text).Match<Result<Test, DomainError>>(
            normalizedText => question.Update(normalizedText, type, points).Match<Result<Test, DomainError>>(
                _ => { MarkChanged(); return this; },
                updateError => updateError),
            error => error);
    }

    public Result<Test, DomainError> RemoveQuestion(QuestionId questionId)
    {
        var editable = EnsureEditable();
        if (editable.TryGetError(out var error)) return error;
        var question = FindQuestion(questionId); if (question is null) return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");
        _questions.Remove(question); MarkChanged(); return this;
    }

    public Result<Test, DomainError> ReorderQuestion(QuestionId questionId, int order)
    {
        var editable = EnsureEditable();
        if (editable.TryGetError(out var error)) return error;
        if (order < 0) return DomainError.Validation("test.question.order", "Question order cannot be negative.");
        var question = FindQuestion(questionId); if (question is null) return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");
        if (_questions.Any(q => q.Id != questionId && q.Order == order)) return DomainError.Conflict("test.question.order_duplicate", "Question order must be unique within a test.");
        question.SetOrder(order); MarkChanged(); return this;
    }

    public Result<AnswerOptionId, DomainError> AddAnswerOption(QuestionId questionId, string text, bool isCorrect, int order)
    {
        var editable = EnsureEditable();
        if (editable.TryGetError(out var error)) return error;
        var question = FindQuestion(questionId); if (question is null) return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");
        var result = question.AddAnswerOption(text, isCorrect, order);
        return result.Match<Result<AnswerOptionId, DomainError>>(id => { MarkChanged(); return id; }, error => error);
    }

    public Result<Test, DomainError> UpdateAnswerOption(QuestionId questionId, AnswerOptionId optionId, string text, bool isCorrect)
    {
        var editable = EnsureEditable();
        if (editable.TryGetError(out var error)) return error;
        var question = FindQuestion(questionId); if (question is null) return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");
        var result = question.UpdateAnswerOption(optionId, text, isCorrect);
        return result.Match<Result<Test, DomainError>>(_ => { MarkChanged(); return this; }, error => error);
    }

    public Result<Test, DomainError> RemoveAnswerOption(QuestionId questionId, AnswerOptionId optionId)
    {
        var editable = EnsureEditable();
        if (editable.TryGetError(out var error)) return error;
        var question = FindQuestion(questionId); if (question is null) return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");
        var result = question.RemoveAnswerOption(optionId);
        return result.Match<Result<Test, DomainError>>(_ => { MarkChanged(); return this; }, error => error);
    }

    public Result<Test, DomainError> ReorderAnswerOption(QuestionId questionId, AnswerOptionId optionId, int order)
    {
        var editable = EnsureEditable();
        if (editable.TryGetError(out var error)) return error;
        var question = FindQuestion(questionId); if (question is null) return DomainError.NotFound("test.question.not_found", "Question was not found in this test.");
        var result = question.ReorderAnswerOption(optionId, order);
        return result.Match<Result<Test, DomainError>>(_ => { MarkChanged(); return this; }, error => error);
    }

    public Result<Test, DomainError> Publish(DateTimeOffset occurredAt)
    {
        var editable = EnsureEditable();
        if (editable.TryGetError(out var error)) return error;
        if (_questions.Count == 0) return DomainError.Validation("test.publish.questions_required", "A test without questions cannot be published.");
        foreach (var question in _questions)
        {
            var validation = question.ValidateForPublication();
            if (validation.TryGetError(out var validationError)) return validationError;
        }
        Status = TestStatus.Published; Touch(); Raise(new TestPublished(Id, occurredAt)); return this;
    }

    public Result<Test, DomainError> Archive()
    {
        if (Status == TestStatus.Archived) return DomainError.Conflict("test.archived", "Test is already archived.");
        Status = TestStatus.Archived; Touch(); return this;
    }

    private Result<Test, DomainError> EnsureEditable() => Status == TestStatus.Archived ? DomainError.Conflict("test.archived", "Archived tests cannot be modified.") : this;
    private Question? FindQuestion(QuestionId questionId) => _questions.SingleOrDefault(q => q.Id == questionId);
    private void MarkChanged() { if (Status == TestStatus.Published) Status = TestStatus.Draft; Touch(); }
    private static Result<string, DomainError> NormalizeTitle(string title) =>
        Normalize(title, "test.title", "Title", TestLimits.TitleMaxLength);

    private static Result<string, DomainError> NormalizeQuestionText(string text) =>
        Normalize(text, "test.question.text", "Question text", TestLimits.QuestionTextMaxLength);

    private static Result<string, DomainError> Normalize(string value, string errorCode, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DomainError.Validation(errorCode, $"{fieldName} is required.");
        var normalized = value.Trim();
        return normalized.Length > maxLength
            ? DomainError.Validation(errorCode, $"{fieldName} cannot exceed {maxLength} characters.")
            : normalized;
    }
}

public sealed class Question : Entity<QuestionId>
{
    private readonly List<AnswerOption> _options = [];
    private Question() { Text = string.Empty; }
    internal Question(QuestionId id, string text, QuestionType type, decimal points, int order) : base(id) { Text = text; Type = type; Points = points; Order = order; }
    public string Text { get; private set; }
    public QuestionType Type { get; private set; }
    public decimal Points { get; private set; }
    public int Order { get; private set; }
    public IReadOnlyCollection<AnswerOption> Options => _options.AsReadOnly();
    internal Result<Question, DomainError> Update(string text, QuestionType type, decimal points)
    {
        // Switching to SingleChoice must not leave behind the multiple correct
        // options that AddAnswerOption/UpdateAnswerOption already refuse to create.
        if (type == QuestionType.SingleChoice && _options.Count(o => o.IsCorrect) > 1)
            return DomainError.Conflict(
                "test.single_choice.multiple_correct",
                "A single-choice question can have only one correct option. Remove the extra correct options before changing the question type.");
        Text = text; Type = type; Points = points;
        return this;
    }
    internal void SetOrder(int order) => Order = order;
    internal Result<AnswerOptionId, DomainError> AddAnswerOption(string text, bool isCorrect, int order) =>
        NormalizeAnswerOptionText(text).Match<Result<AnswerOptionId, DomainError>>(
            normalizedText =>
            {
                if (order < 0) return DomainError.Validation("test.answer_option.order", "Answer option order cannot be negative.");
                if (_options.Any(o => o.Order == order)) return DomainError.Conflict("test.answer_option.order_duplicate", "Answer option order must be unique within a question.");
                if (_options.Any(o => string.Equals(o.Text, normalizedText, StringComparison.OrdinalIgnoreCase))) return DomainError.Conflict("test.answer_option.text_duplicate", "Answer option text must be unique within a question.");
                if (Type == QuestionType.SingleChoice && isCorrect && _options.Any(o => o.IsCorrect)) return DomainError.Conflict("test.single_choice.multiple_correct", "A single-choice question can have only one correct option.");
                var option = new AnswerOption(AnswerOptionId.New(), normalizedText, isCorrect, order);
                _options.Add(option);
                return option.Id;
            },
            error => error);

    internal Result<Question, DomainError> UpdateAnswerOption(AnswerOptionId optionId, string text, bool isCorrect) =>
        NormalizeAnswerOptionText(text).Match<Result<Question, DomainError>>(
            normalizedText =>
            {
                var option = _options.SingleOrDefault(o => o.Id == optionId);
                if (option is null) return DomainError.NotFound("test.answer_option.not_found", "Answer option was not found in this question.");
                if (_options.Any(o => o.Id != optionId && string.Equals(o.Text, normalizedText, StringComparison.OrdinalIgnoreCase))) return DomainError.Conflict("test.answer_option.text_duplicate", "Answer option text must be unique within a question.");
                if (Type == QuestionType.SingleChoice && isCorrect && _options.Any(o => o.Id != optionId && o.IsCorrect)) return DomainError.Conflict("test.single_choice.multiple_correct", "A single-choice question can have only one correct option.");
                option.Update(normalizedText, isCorrect);
                return this;
            },
            error => error);
    internal Result<Question, DomainError> RemoveAnswerOption(AnswerOptionId optionId) { var option = _options.SingleOrDefault(o => o.Id == optionId); if (option is null) return DomainError.NotFound("test.answer_option.not_found", "Answer option was not found in this question."); _options.Remove(option); return this; }
    internal Result<Question, DomainError> ReorderAnswerOption(AnswerOptionId optionId, int order)
    {
        if (order < 0) return DomainError.Validation("test.answer_option.order", "Answer option order cannot be negative.");
        var option = _options.SingleOrDefault(o => o.Id == optionId); if (option is null) return DomainError.NotFound("test.answer_option.not_found", "Answer option was not found in this question.");
        if (_options.Any(o => o.Id != optionId && o.Order == order)) return DomainError.Conflict("test.answer_option.order_duplicate", "Answer option order must be unique within a question.");
        option.SetOrder(order); return this;
    }
    internal Result<Question, DomainError> ValidateForPublication()
    {
        if (!Enum.IsDefined(typeof(QuestionType), Type)) return DomainError.Validation("test.question.type", "Question type is not supported.");
        if (_options.Count < 2) return DomainError.Validation("test.question.options_required", "Choice questions must contain at least two answer options.");
        var correctCount = _options.Count(o => o.IsCorrect);
        if (Type == QuestionType.SingleChoice && correctCount != 1) return DomainError.Validation("test.single_choice.correct_count", "A single-choice question must contain exactly one correct answer option.");
        if (Type == QuestionType.MultipleChoice && correctCount < 1) return DomainError.Validation("test.multiple_choice.correct_required", "A multiple-choice question must contain at least one correct answer option.");
        return this;
    }

    private static Result<string, DomainError> NormalizeAnswerOptionText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DomainError.Validation("test.answer_option.text", "Answer option text is required.");
        var normalized = text.Trim();
        return normalized.Length > TestLimits.AnswerOptionTextMaxLength
            ? DomainError.Validation("test.answer_option.text", $"Answer option text cannot exceed {TestLimits.AnswerOptionTextMaxLength} characters.")
            : normalized;
    }
}

public sealed class AnswerOption : Entity<AnswerOptionId>
{
    private AnswerOption() { Text = string.Empty; }
    internal AnswerOption(AnswerOptionId id, string text, bool isCorrect, int order) : base(id) { Text = text; IsCorrect = isCorrect; Order = order; }
    public string Text { get; private set; }
    public bool IsCorrect { get; private set; }
    public int Order { get; private set; }
    internal void Update(string text, bool isCorrect) { Text = text; IsCorrect = isCorrect; }
    internal void SetOrder(int order) => Order = order;
}

public enum QuestionType { SingleChoice = 1, MultipleChoice = 2 }
public enum TestStatus { Draft = 0, Published = 1, Archived = 2 }
