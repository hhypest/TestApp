namespace TestApp.Messaging.Abstractions;

public interface IDispatcher
{
    public Task Send<TCommand>(TCommand command, CancellationToken ct) where TCommand : ICommand;
    public Task<TResponse> Send<TCommand, TResponse>(TCommand command, CancellationToken ct) where TCommand : ICommand<TResponse>;
    public Task<TResponse> Query<TQuery, TResponse>(TQuery query, CancellationToken ct) where TQuery : IQuery<TResponse>;
    public Task Publish<TNotify>(TNotify notify, CancellationToken ct) where TNotify : INotification;
}