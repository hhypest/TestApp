using TestApp.Domain.Events;

namespace TestApp.Domain.Entities;

public abstract class Entity
{
    private readonly IList<IDomainEvent> _events = [];
    public IReadOnlyCollection<IDomainEvent> Events => [.._events];

    protected void Raise(IDomainEvent domainEvent)
    {
        _events.Add(domainEvent);
    }

    public void ClearEvents()
    {
        _events.Clear();
    }
}