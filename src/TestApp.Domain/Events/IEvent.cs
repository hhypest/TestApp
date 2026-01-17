namespace TestApp.Domain.Events;

public interface IEvent
{
    public Guid EventId { get; }
    public DateTimeOffset OccurredAt { get; }
}

public interface IDomainEvent : IEvent;