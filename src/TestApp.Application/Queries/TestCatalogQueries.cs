using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Queries;

public sealed record TestCatalogItem(
    TestId Id,
    string Title,
    TestStatus Status,
    decimal PassingPercentage,
    int? TimeLimitMinutes,
    int QuestionCount,
    int PublishedRevisionCount,
    int? LatestRevisionVersion,
    DateTimeOffset? LatestPublishedAt);

public sealed record PublishedRevisionSummary(
    PublishedTestRevisionId Id,
    TestId TestId,
    int Version,
    string Title,
    decimal PassingPercentage,
    int? TimeLimitMinutes,
    DateTimeOffset PublishedAt);

public sealed record GetTestsQuery(
    int Page = 1,
    int PageSize = 20,
    TestStatus? Status = null,
    string? Search = null) : IQuery<PagedResult<TestCatalogItem>>;

public sealed record GetTestRevisionsQuery(TestId TestId) : IQuery<IReadOnlyList<PublishedRevisionSummary>>;

public interface ITestCatalogQueries
{
    Task<PagedResult<TestCatalogItem>> GetTestsAsync(
        int page,
        int pageSize,
        TestStatus? status,
        string? search,
        CancellationToken ct);

    Task<IReadOnlyList<PublishedRevisionSummary>> GetRevisionsAsync(TestId testId, CancellationToken ct);
}

public sealed class GetTestsQueryHandler(ITestCatalogQueries queries)
    : IQueryHandler<GetTestsQuery, PagedResult<TestCatalogItem>>
{
    public Task<PagedResult<TestCatalogItem>> Handle(GetTestsQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        return queries.GetTestsAsync(page, pageSize, query.Status, search, ct);
    }
}

public sealed class GetTestRevisionsQueryHandler(ITestCatalogQueries queries)
    : IQueryHandler<GetTestRevisionsQuery, IReadOnlyList<PublishedRevisionSummary>>
{
    public Task<IReadOnlyList<PublishedRevisionSummary>> Handle(GetTestRevisionsQuery query, CancellationToken ct) =>
        queries.GetRevisionsAsync(query.TestId, ct);
}
