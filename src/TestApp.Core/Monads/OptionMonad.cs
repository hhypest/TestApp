namespace TestApp.Core.Monads;

public readonly struct Option<TSome>
    where TSome : notnull
{
    private readonly TSome _some;
    private readonly bool _state;

    private readonly bool IsSome => _state;

    private Option(TSome some)
    {
        _some = some;
        _state = true;
    }

    public static Option<TSome> Some(TSome some)
    {
        ArgumentNullException.ThrowIfNull(some);
        return new(some);
    }
    public static Option<TSome> None => default;

    public static implicit operator Option<TSome>(TSome some) => Some(some);

    public Option<TNewSome> Map<TNewSome>(Func<TSome, TNewSome> mapFunc)
        where TNewSome : notnull
    {
        return IsSome switch
        {
            true => new(mapFunc(_some)),
            false => default
        };
    }

    public Option<TNewSome> Bind<TNewSome>(Func<TSome, Option<TNewSome>> bindFunc)
        where TNewSome : notnull
    {
        return IsSome switch
        {
            true => bindFunc(_some),
            false => default
        };
    }

    public Option<TSome> Tap(Action<TSome> action)
    {
        if (IsSome)
            action(_some);

        return this;

    }

    public TResult Match<TResult>(Func<TSome, TResult> onSome, Func<TResult> onNone)
    {
        return IsSome switch
        {
            true => onSome(_some),
            false => onNone()
        };
    }

    public Task<TResult> Match<TResult>(Func<TSome, Task<TResult>> onSome, Func<Task<TResult>> onNone)
    {
        return IsSome switch
        {
            true => onSome(_some),
            false => onNone()
        };
    }

    public Task<TResult> Match<TResult>(Func<TSome, CancellationToken, Task<TResult>> onSome, Func<CancellationToken, Task<TResult>> onNone, CancellationToken token)
    {
        return IsSome switch
        {
            true => onSome(_some, token),
            false => onNone(token)
        };
    }

    public ValueTask<TResult> Match<TResult>(Func<TSome, ValueTask<TResult>> onSome, Func<ValueTask<TResult>> onNone)
    {
        return IsSome switch
        {
            true => onSome(_some),
            false => onNone()
        };
    }

    public ValueTask<TResult> Match<TResult>(Func<TSome, CancellationToken, ValueTask<TResult>> onSome, Func<CancellationToken, ValueTask<TResult>> onNone, CancellationToken token)
    {
        return IsSome switch
        {
            true => onSome(_some, token),
            false => onNone(token)
        };
    }
}