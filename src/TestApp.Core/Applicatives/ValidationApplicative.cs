using System.Collections.Immutable;
using TestApp.Core.Data;

namespace TestApp.Core.Applicatives;

public readonly struct Validation<TError, TValue>
    where TError : notnull, Error
    where TValue : notnull
{
    private readonly ImmutableArray<TError> _errors;
    private readonly TValue _value;
    private readonly bool _isValid;

    private Validation(TValue value)
    {
        _value = value;
        _errors = [];
        _isValid = true;
    }

    private Validation(ImmutableArray<TError> errors)
    {
        _value = default!;
        _errors = errors;
        _isValid = false;
    }

    public readonly bool IsValid => _isValid;
    public readonly bool IsInvalid => !_isValid;

    public static Validation<TError, TValue> Valid(TValue value) => new(value);
    public static Validation<TError, TValue> Invalid(IEnumerable<TError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return CreateInvalid([.. errors]);
    }
    public static Validation<TError, TValue> Invalid(params TError[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return CreateInvalid([.. errors]);
    }
    public static Validation<TError, TValue> Invalid(TError error) => new([error]);

    private static Validation<TError, TValue> CreateInvalid(ImmutableArray<TError> errors)
    {
        if (errors.IsDefaultOrEmpty)
            throw new ArgumentException("Validation.Invalid requires at least one error.", nameof(errors));

        return new(errors);
    }

    public Validation<TError, TNewValue> Map<TNewValue>(Func<TValue, TNewValue> map)
        where TNewValue : notnull
    {
        return IsValid switch
        {
            true => Validation<TError, TNewValue>.Valid(map(_value)),
            false => Validation<TError, TNewValue>.Invalid(_errors)
        };
    }

    public Validation<TError, TNewValue> Apply<TNewValue>(Validation<TError, Func<TValue, TNewValue>> apply)
        where TNewValue : notnull
    {
        if (IsValid && apply.IsValid)
            return Validation<TError, TNewValue>.Valid(apply._value(_value));

        if (IsInvalid && apply.IsInvalid)
            return Validation<TError, TNewValue>.Invalid(_errors.AddRange(apply._errors));

        return IsInvalid switch
        {
            true => Validation<TError, TNewValue>.Invalid(_errors),
            false => Validation<TError, TNewValue>.Invalid(apply._errors)
        };
    }

    public Validation<TError, TValue> Ensure(Predicate<TValue> predicate, Func<TError> errorFactory)
    {
        if (IsInvalid)
            return this;

        return predicate(_value) switch
        {
            true => this,
            false => Validation<TError, TValue>.Invalid(errorFactory())
        };
    }

    public Validation<TError, TValue> Ensure(Predicate<TValue> predicate, TError error)
    {
        if (IsInvalid)
            return this;

        return predicate(_value) switch
        {
            true => this,
            false => Validation<TError, TValue>.Invalid(error)
        };
    }

    public TResult Match<TResult>(Func<TValue, TResult> onValid, Func<IReadOnlyCollection<TError>, TResult> onInvalid)
    {
        return IsValid switch
        {
            true => onValid(_value),
            false => onInvalid(_errors)
        };

    }

    public Task<TResult> Match<TResult>(Func<TValue, Task<TResult>> onValid, Func<IReadOnlyCollection<TError>, Task<TResult>> onInvalid)
    {
        return IsValid switch
        {
            true => onValid(_value),
            false => onInvalid(_errors)
        };

    }

    public Task<TResult> Match<TResult>(Func<TValue, CancellationToken, Task<TResult>> onValid, Func<IReadOnlyCollection<TError>, CancellationToken, Task<TResult>> onInvalid, CancellationToken token)
    {
        return IsValid switch
        {
            true => onValid(_value, token),
            false => onInvalid(_errors, token)
        };

    }

    public ValueTask<TResult> Match<TResult>(Func<TValue, ValueTask<TResult>> onValid, Func<IReadOnlyCollection<TError>, ValueTask<TResult>> onInvalid)
    {
        return IsValid switch
        {
            true => onValid(_value),
            false => onInvalid(_errors)
        };

    }

    public ValueTask<TResult> Match<TResult>(Func<TValue, CancellationToken, ValueTask<TResult>> onValid, Func<IReadOnlyCollection<TError>, CancellationToken, ValueTask<TResult>> onInvalid, CancellationToken token)
    {
        return IsValid switch
        {
            true => onValid(_value, token),
            false => onInvalid(_errors, token)
        };

    }
}
