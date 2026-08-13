namespace TestApp.Domain.Events;

public abstract record DomainEvent : IDomainEvent
{
    protected DomainEvent(DateTimeOffset occurredAt)
    {
        EventId = Guid.CreateVersion7();
        OccurredAt = occurredAt.ToUniversalTime();
    }

    public Guid EventId { get; }
    public DateTimeOffset OccurredAt { get; }
}
