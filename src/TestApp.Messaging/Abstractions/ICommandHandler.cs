namespace TestApp.Messaging.Abstractions;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public Task<TResponse> Handle(TCommand command, CancellationToken ct);
}
