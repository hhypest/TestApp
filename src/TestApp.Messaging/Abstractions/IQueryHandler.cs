namespace TestApp.Messaging.Abstractions;

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public Task<TResponse> Handle(TQuery query, CancellationToken ct);
}