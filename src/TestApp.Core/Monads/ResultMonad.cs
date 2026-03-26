namespace TestApp.Core.Monads;

public readonly struct Result<TSuccess, TFailure>
    where TSuccess : notnull
    where TFailure : notnull
{
    private readonly TSuccess _success;
    private readonly TFailure _failure;
    private readonly bool _state;

    private bool IsSuccess => _state;
    private bool IsFailure => !_state;

    private Result(TSuccess success)
    {
        _success = success ?? throw new ArgumentNullException(nameof(success));
        _failure = default!;
        _state = true;
    }

    private Result(TFailure failure)
    {
        _failure = failure ?? throw new ArgumentNullException(nameof(failure));
        _success = default!;
        _state = false;
    }

    public static Result<TSuccess, TFailure> Success(TSuccess value) => new(value);
    public static Result<TSuccess, TFailure> Failure(TFailure error) => new(error);

    public static implicit operator Result<TSuccess, TFailure>(TSuccess success) => Success(success);

    public static implicit operator Result<TSuccess, TFailure>(TFailure failure) => Failure(failure);

    public Result<TNewSuccess, TFailure> Map<TNewSuccess>(Func<TSuccess, TNewSuccess> mapFunc)
        where TNewSuccess : notnull
    {
        return IsSuccess switch
        {
            true => new(mapFunc(_success)),
            false => new(_failure)
        };
    }

    public Result<TNewSuccess, TFailure> Bind<TNewSuccess>(Func<TSuccess, Result<TNewSuccess, TFailure>> bindFunc)
        where TNewSuccess : notnull
    {
        return IsSuccess switch
        {
            true => bindFunc(_success),
            false => new(_failure)
        };
    }

    public Result<TSuccess, TFailure> Ensure(Predicate<TSuccess> predicate, Func<TFailure> failureFactory)
    {
        if (IsFailure)
            return this;

        return (IsSuccess && predicate(_success)) switch
        {
            true => this,
            false => new(failureFactory())
        };
    }

    public Result<TSuccess, TFailure> Tap(Action<TSuccess> action)
    {
        if (IsSuccess)
            action(_success);

        return this;
    }

    public TResult Match<TResult>(Func<TSuccess, TResult> onSuccess, Func<TFailure, TResult> onFailure)
    {
        return IsSuccess switch
        {
            true => onSuccess(_success),
            false => onFailure(_failure)
        };
    }

    public Task<TResult> Match<TResult>(Func<TSuccess, Task<TResult>> onSuccess, Func<TFailure, Task<TResult>> onFailure)
    {
        return IsSuccess switch
        {
            true => onSuccess(_success),
            false => onFailure(_failure)
        };
    }

    public Task<TResult> Match<TResult>(Func<TSuccess, CancellationToken, Task<TResult>> onSuccess, Func<TFailure, CancellationToken, Task<TResult>> onFailure, CancellationToken token)
    {
        return IsSuccess switch
        {
            true => onSuccess(_success, token),
            false => onFailure(_failure, token)
        };
    }

    public ValueTask<TResult> Match<TResult>(Func<TSuccess, ValueTask<TResult>> onSuccess, Func<TFailure, ValueTask<TResult>> onFailure)
    {
        return IsSuccess switch
        {
            true => onSuccess(_success),
            false => onFailure(_failure)
        };
    }

    public ValueTask<TResult> Match<TResult>(Func<TSuccess, CancellationToken, ValueTask<TResult>> onSuccess, Func<TFailure, CancellationToken, ValueTask<TResult>> onFailure, CancellationToken token)
    {
        return IsSuccess switch
        {
            true => onSuccess(_success, token),
            false => onFailure(_failure, token)
        };
    }
}