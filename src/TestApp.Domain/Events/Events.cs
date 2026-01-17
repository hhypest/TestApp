namespace TestApp.Domain.Events;

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId => Guid.CreateVersion7();
    public DateTimeOffset OccurredAt => DateTimeOffset.UtcNow;
}