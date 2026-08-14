using TestApp.Application.Abstractions;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Queries;

public sealed record AnswerOptionEditorView(
    AnswerOptionId Id,
    string Text,
    bool IsCorrect,
    int Order);

public sealed record QuestionEditorView(
    QuestionId Id,
    string Text,
    QuestionType Type,
    decimal Points,
    int Order,
    IReadOnlyList<AnswerOptionEditorView> Options);

public sealed record TestEditorView(
    TestId Id,
    string Title,
    TestStatus Status,
    long ConcurrencyVersion,
    decimal PassingPercentage,
    int? TimeLimitMinutes,
    IReadOnlyList<QuestionEditorView> Questions);

public sealed record GetTestEditorViewQuery(TestId TestId) : IQuery<TestEditorView?>;

public sealed class GetTestEditorViewQueryHandler(IReadModelQueries queries, ICurrentActor actor)
    : IQueryHandler<GetTestEditorViewQuery, TestEditorView?>
{
    public Task<TestEditorView?> Handle(GetTestEditorViewQuery query, CancellationToken ct) =>
        queries.GetTestEditorViewAsync(query.TestId, AuthorReadScope.OwnerFilter(actor), ct);
}
