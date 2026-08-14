using TestApp.Application.Abstractions;
using TestApp.Domain.Assignments;
using TestApp.Domain.Revisions;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Queries;

public sealed record AssignmentSummary(
    TestAssignmentId Id,
    PublishedTestRevisionId RevisionId,
    string TestTitle,
    int RevisionVersion,
    decimal PassingPercentage,
    int? TimeLimitMinutes,
    DateTimeOffset AvailableFrom,
    DateTimeOffset? AvailableUntil,
    int? AttemptLimit,
    AssignmentStatus Status);

public sealed record GetMyAssignmentsQuery(
    int Page = 1,
    int PageSize = 20,
    AssignmentStatus? Status = null) : IQuery<PagedResult<AssignmentSummary>>;

public sealed class GetMyAssignmentsQueryHandler(IReadModelQueries queries, ICurrentActor actor)
    : IQueryHandler<GetMyAssignmentsQuery, PagedResult<AssignmentSummary>>
{
    public Task<PagedResult<AssignmentSummary>> Handle(GetMyAssignmentsQuery query, CancellationToken ct)
    {
        var (page, size) = Paging.Normalize(query.Page, query.PageSize);
        return queries.GetAssignmentsAsync(actor.UserId, actor.Groups, page, size, query.Status, ct);
    }
}
