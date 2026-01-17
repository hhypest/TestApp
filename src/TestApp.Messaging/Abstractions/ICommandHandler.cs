namespace TestApp.Messaging.Abstractions;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    public Task Handle(TCommand command, CancellationToken ct);
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public Task<TResponse> Handle(TCommand command, CancellationToken ct);
}