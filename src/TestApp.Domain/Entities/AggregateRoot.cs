using TestApp.Domain.Events;

namespace TestApp.Domain.Entities;

public interface IAggregateRoot
{
    long ConcurrencyVersion { get; }
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot() { }
    protected AggregateRoot(TId id) : base(id) { }

    public long ConcurrencyVersion { get; private set; }
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    protected void Touch() => ConcurrencyVersion++;

    public void ClearDomainEvents() => _domainEvents.Clear();
}
