using TestApp.Application.Abstractions;
using TestApp.Domain.Identity;

namespace TestApp.Application.Queries;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

internal static class Paging
{
    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
}

internal static class AuthorReadScope
{
    public static ExternalUserId? OwnerFilter(ICurrentActor actor) =>
        actor.Roles.Any(role => string.Equals(role, "test-admin", StringComparison.OrdinalIgnoreCase))
            ? null
            : actor.UserId;
}
