namespace TestApp.Domain.Events;

public interface IEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

/// <summary>Internal business notification raised by an aggregate.</summary>
public interface IDomainEvent : IEvent;

/// <summary>
/// Explicit contract for events intended to cross the application boundary.
/// Only these events are eligible for transactional Outbox persistence.
/// </summary>
public interface IIntegrationEvent : IDomainEvent;
