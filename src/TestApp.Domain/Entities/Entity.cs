namespace TestApp.Domain.Entities;

public abstract class Entity<TId> where TId : notnull
{
    protected Entity() { Id = default!; }
    protected Entity(TId id) => Id = id ?? throw new ArgumentNullException(nameof(id));

    public TId Id { get; private set; }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other || GetType() != other.GetType()) return false;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
