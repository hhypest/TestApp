using Microsoft.EntityFrameworkCore;
using TestApp.Application.Queries;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Infrastructure.Persistence;

public sealed class TestCatalogQueries(AppDbContext db) : ITestCatalogQueries
{
    public async Task<PagedResult<TestCatalogItem>> GetTestsAsync(
        int page,
        int pageSize,
        TestStatus? status,
        string? search,
        CancellationToken ct)
    {
        var query = db.Tests.AsNoTracking().AsQueryable();
        if (status is { } statusValue)
            query = query.Where(x => x.Status == statusValue);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Title.Contains(search));

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(x => x.Title)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Status,
                x.Settings.PassingPercentage,
                x.Settings.TimeLimitMinutes,
                QuestionCount = x.Questions.Count
            })
            .ToArrayAsync(ct);

        var testIds = rows.Select(x => x.Id).ToArray();
        var revisionMetadata = await db.Revisions.AsNoTracking()
            .Where(x => testIds.Contains(x.TestId))
            .Select(x => new { x.TestId, x.Version, x.PublishedAt })
            .ToArrayAsync(ct);

        var revisionsByTest = revisionMetadata
            .GroupBy(x => x.TestId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(r => r.Version).ToArray());

        var items = rows.Select(row =>
        {
            revisionsByTest.TryGetValue(row.Id, out var revisions);
            var latest = revisions?.FirstOrDefault();
            return new TestCatalogItem(
                row.Id,
                row.Title,
                row.Status,
                row.PassingPercentage,
                row.TimeLimitMinutes,
                row.QuestionCount,
                revisions?.Length ?? 0,
                latest?.Version,
                latest?.PublishedAt);
        }).ToArray();

        return new PagedResult<TestCatalogItem>(items, page, pageSize, totalCount);
    }

    public async Task<IReadOnlyList<PublishedRevisionSummary>> GetRevisionsAsync(TestId testId, CancellationToken ct) =>
        await db.Revisions.AsNoTracking()
            .Where(x => x.TestId == testId)
            .OrderByDescending(x => x.Version)
            .Select(x => new PublishedRevisionSummary(
                x.Id,
                x.TestId,
                x.Version,
                x.Title,
                x.PassingPercentage,
                x.TimeLimitMinutes,
                x.PublishedAt))
            .ToArrayAsync(ct);
}
