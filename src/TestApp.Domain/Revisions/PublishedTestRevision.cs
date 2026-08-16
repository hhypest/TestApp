using System.Text.Json.Serialization;
using TestApp.Core.Monads;
using TestApp.Domain.Attempts;
using TestApp.Domain.Common;
using TestApp.Domain.Entities;
using TestApp.Domain.Tests;

namespace TestApp.Domain.Revisions;

public readonly record struct PublishedTestRevisionId
{
    [JsonConstructor]
    public PublishedTestRevisionId(Guid value) => Value = StrongIdGuard.Ensure(value, "Revision id", nameof(value));

    public Guid Value { get; }

    public static PublishedTestRevisionId New() => new(Guid.CreateVersion7());
}

public sealed record PublishedAnswerOption(AnswerOptionId Id, string Text, int Order, bool IsCorrect);

public sealed record PublishedQuestion(
    QuestionId Id,
    string Text,
    QuestionType Type,
    decimal Points,
    int Order,
    IReadOnlyCollection<PublishedAnswerOption> Options);

public sealed class PublishedTestRevision : AggregateRoot<PublishedTestRevisionId>
{
    private readonly List<PublishedQuestion> _questions = [];

    public TestId TestId { get; private set; }
    public int Version { get; private set; }
    public string Title { get; private set; }
    public decimal PassingPercentage { get; private set; }
    public int? TimeLimitMinutes { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }
    public IReadOnlyCollection<PublishedQuestion> Questions => _questions.AsReadOnly();

    private PublishedTestRevision() { Title = string.Empty; }

    private PublishedTestRevision(
        PublishedTestRevisionId id,
        TestId testId,
        int version,
        string title,
        decimal passingPercentage,
        int? timeLimitMinutes,
        DateTimeOffset publishedAt,
        IEnumerable<PublishedQuestion> questions) : base(id)
    {
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        TestId = testId;
        Version = version;
        Title = title;
        PassingPercentage = passingPercentage;
        TimeLimitMinutes = timeLimitMinutes;
        PublishedAt = publishedAt.ToUniversalTime();
        _questions.AddRange(questions);
    }

    public static PublishedTestRevision From(Test test, PublishedTestRevisionId id, int version, DateTimeOffset publishedAt)
    {
        var questions = test.Questions
            .OrderBy(q => q.Order)
            .Select(q => new PublishedQuestion(
                q.Id,
                q.Text,
                q.Type,
                q.Points,
                q.Order,
                q.Options.OrderBy(o => o.Order)
                    .Select(o => new PublishedAnswerOption(o.Id, o.Text, o.Order, o.IsCorrect))
                    .ToArray()))
            .ToArray();

        return new PublishedTestRevision(
            id,
            test.Id,
            version,
            test.Title,
            test.Settings.PassingPercentage,
            test.Settings.TimeLimitMinutes,
            publishedAt,
            questions);
    }

    public DateTimeOffset? CalculateDeadline(DateTimeOffset startedAt) =>
        TimeLimitMinutes is { } minutes ? startedAt.AddMinutes(minutes) : null;

    public bool IsPassed(AttemptScore score) => score.Percentage >= PassingPercentage;

    public Result<PublishedQuestion, DomainError> ValidateAnswer(QuestionId questionId, IEnumerable<AnswerOptionId> selectedOptionIds)
    {
        ArgumentNullException.ThrowIfNull(selectedOptionIds);
        var question = _questions.SingleOrDefault(q => q.Id == questionId);
        if (question is null) return DomainError.NotFound("revision.question.not_found", "Question does not exist in this published revision.");
        var selected = selectedOptionIds.Distinct().ToArray();
        if (selected.Length == 0) return DomainError.Validation("revision.answer.empty", "At least one answer option must be selected.");
        if (question.Type == QuestionType.SingleChoice && selected.Length != 1) return DomainError.Validation("revision.single_choice.selection_count", "Single-choice question requires exactly one selected option.");
        var validIds = question.Options.Select(o => o.Id).ToHashSet();
        if (selected.Any(id => !validIds.Contains(id))) return DomainError.Validation("revision.answer.invalid_option", "One or more selected options do not belong to this question.");
        return question;
    }

    public AttemptScore CalculateScore(IEnumerable<QuestionResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(responses);
        var responseMap = responses.ToDictionary(r => r.Id);
        decimal earned = 0;
        decimal maximum = 0;
        foreach (var question in _questions)
        {
            maximum += question.Points;
            if (!responseMap.TryGetValue(question.Id, out var response)) continue;
            var selected = response.SelectedOptions.Select(x => x.OptionId).ToHashSet();
            var correct = question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
            if (selected.SetEquals(correct)) earned += question.Points;
        }
        return new AttemptScore(earned, maximum);
    }
}
