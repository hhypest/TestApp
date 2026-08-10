using TestApp.Domain.Entities;
using TestApp.Domain.Tests;

namespace TestApp.Domain.Revisions;

public readonly record struct PublishedTestRevisionId(Guid Value)
{
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
    public DateTimeOffset PublishedAt { get; private set; }
    public IReadOnlyCollection<PublishedQuestion> Questions => _questions.AsReadOnly();

    private PublishedTestRevision()
    {
        Title = string.Empty;
    }

    private PublishedTestRevision(
        PublishedTestRevisionId id,
        TestId testId,
        int version,
        string title,
        DateTimeOffset publishedAt,
        IEnumerable<PublishedQuestion> questions)
    {
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));

        Id = id;
        TestId = testId;
        Version = version;
        Title = title;
        PublishedAt = publishedAt;
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
                q.Options
                    .OrderBy(o => o.Order)
                    .Select(o => new PublishedAnswerOption(o.Id, o.Text, o.Order, o.IsCorrect))
                    .ToArray()))
            .ToArray();

        return new PublishedTestRevision(id, test.Id, version, test.Title, publishedAt, questions);
    }
}
