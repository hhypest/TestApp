namespace TestApp.Messaging.Abstractions;

public interface INotificationHandler<in TNotify>
    where TNotify : INotification
{
    public Task Handle(TNotify notify, CancellationToken ct);
}