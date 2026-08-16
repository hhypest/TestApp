using TestApp.Core.Monads;
using Xunit;

namespace TestApp.Core.Tests;

/// <summary>
/// Behaviour of <see cref="Result{TSuccess,TFailure}"/>, the failure contract every
/// domain and application rule in TestApp is expressed through.
/// </summary>
public sealed class ResultMonadTests
{
    private sealed record Failure(string Code);

    private static Result<int, Failure> Ok(int value = 1) => Result<int, Failure>.Success(value);
    private static Result<int, Failure> Err(string code = "boom") => Result<int, Failure>.Failure(new Failure(code));

    // ------------------------------------------------------------ construction

    [Fact]
    public void Success_and_failure_report_their_state()
    {
        Assert.True(Ok().IsSuccess);
        Assert.False(Ok().IsFailure);
        Assert.True(Err().IsFailure);
        Assert.False(Err().IsSuccess);
    }

    [Fact]
    public void Implicit_conversion_creates_a_success_from_a_value()
    {
        Result<int, Failure> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Match(value => value, _ => -1));
    }

    [Fact]
    public void Implicit_conversion_creates_a_failure_from_an_error()
    {
        Result<int, Failure> result = new Failure("bad");

        Assert.True(result.IsFailure);
        Assert.Equal("bad", result.Match(_ => "none", error => error.Code));
    }

    [Fact]
    public void A_null_success_value_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string, Failure>.Success(null!));
    }

    [Fact]
    public void A_null_failure_value_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int, Failure>.Failure(null!));
    }

    // ------------------------------------------------------------ TryGetError

    [Fact]
    public void TryGetError_yields_the_error_for_a_failure()
    {
        Assert.True(Err("code-1").TryGetError(out var error));
        Assert.Equal("code-1", error.Code);
    }

    [Fact]
    public void TryGetError_returns_false_for_a_success()
    {
        Assert.False(Ok().TryGetError(out _));
    }

    [Fact]
    public void TryGetError_leaves_no_error_value_behind_on_success()
    {
        // The out parameter must not be observable as a real error when the
        // result succeeded; callers rely on the bool alone.
        var succeeded = Ok().TryGetError(out var error);

        Assert.False(succeeded);
        Assert.Null(error);
    }

    // -------------------------------------------------------------------- Map

    [Fact]
    public void Map_transforms_a_success_value()
    {
        var mapped = Ok(2).Map(value => value * 21);

        Assert.Equal(42, mapped.Match(value => value, _ => -1));
    }

    [Fact]
    public void Map_leaves_a_failure_untouched_and_does_not_run_the_projection()
    {
        var invoked = false;

        var mapped = Err("stays").Map(value => { invoked = true; return value * 2; });

        Assert.False(invoked);
        Assert.Equal("stays", mapped.Match(_ => "none", error => error.Code));
    }

    // ------------------------------------------------------------------- Bind

    [Fact]
    public void Bind_chains_a_successful_computation()
    {
        var bound = Ok(2).Bind(value => Result<string, Failure>.Success($"v{value}"));

        Assert.Equal("v2", bound.Match(value => value, _ => "none"));
    }

    [Fact]
    public void Bind_propagates_a_failure_produced_by_the_continuation()
    {
        var bound = Ok(2).Bind(_ => Result<string, Failure>.Failure(new Failure("inner")));

        Assert.Equal("inner", bound.Match(_ => "none", error => error.Code));
    }

    [Fact]
    public void Bind_short_circuits_on_an_existing_failure()
    {
        var invoked = false;

        var bound = Err("outer").Bind(value =>
        {
            invoked = true;
            return Result<string, Failure>.Success($"v{value}");
        });

        Assert.False(invoked);
        Assert.Equal("outer", bound.Match(_ => "none", error => error.Code));
    }

    // ----------------------------------------------------------------- Ensure

    [Fact]
    public void Ensure_keeps_a_success_that_satisfies_the_predicate()
    {
        var ensured = Ok(10).Ensure(value => value > 5, () => new Failure("too-small"));

        Assert.True(ensured.IsSuccess);
    }

    [Fact]
    public void Ensure_converts_a_success_that_fails_the_predicate_into_a_failure()
    {
        var ensured = Ok(1).Ensure(value => value > 5, () => new Failure("too-small"));

        Assert.Equal("too-small", ensured.Match(_ => "none", error => error.Code));
    }

    [Fact]
    public void Ensure_does_not_evaluate_the_predicate_or_factory_on_an_existing_failure()
    {
        var predicateInvoked = false;
        var factoryInvoked = false;

        var ensured = Err("original").Ensure(
            _ => { predicateInvoked = true; return false; },
            () => { factoryInvoked = true; return new Failure("replacement"); });

        Assert.False(predicateInvoked);
        Assert.False(factoryInvoked);
        Assert.Equal("original", ensured.Match(_ => "none", error => error.Code));
    }

    // -------------------------------------------------------------------- Tap

    [Fact]
    public void Tap_runs_the_side_effect_for_a_success_and_returns_the_original()
    {
        var seen = 0;

        var tapped = Ok(7).Tap(value => seen = value);

        Assert.Equal(7, seen);
        Assert.Equal(7, tapped.Match(value => value, _ => -1));
    }

    [Fact]
    public void Tap_skips_the_side_effect_for_a_failure()
    {
        var invoked = false;

        var tapped = Err().Tap(_ => invoked = true);

        Assert.False(invoked);
        Assert.True(tapped.IsFailure);
    }

    // ------------------------------------------------------------------ Match

    [Fact]
    public void Match_selects_the_branch_matching_the_state()
    {
        Assert.Equal("ok", Ok().Match(_ => "ok", _ => "err"));
        Assert.Equal("err", Err().Match(_ => "ok", _ => "err"));
    }

    [Fact]
    public async Task Async_match_selects_the_success_branch()
    {
        var value = await Ok(5).Match(
            success => Task.FromResult(success * 2),
            _ => Task.FromResult(-1));

        Assert.Equal(10, value);
    }

    [Fact]
    public async Task Async_match_selects_the_failure_branch()
    {
        var value = await Err().Match(
            success => Task.FromResult(success * 2),
            _ => Task.FromResult(-1));

        Assert.Equal(-1, value);
    }

    [Fact]
    public async Task Cancellable_async_match_forwards_the_token_to_the_taken_branch()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken observed = default;

        await Ok().Match(
            (_, token) => { observed = token; return Task.FromResult(0); },
            (_, _) => Task.FromResult(-1),
            cts.Token);

        Assert.Equal(cts.Token, observed);
    }

    [Fact]
    public async Task ValueTask_match_selects_the_branch_matching_the_state()
    {
        var success = await Ok(3).Match(
            value => ValueTask.FromResult(value),
            _ => ValueTask.FromResult(-1));
        var failure = await Err().Match(
            value => ValueTask.FromResult(value),
            _ => ValueTask.FromResult(-1));

        Assert.Equal(3, success);
        Assert.Equal(-1, failure);
    }

    [Fact]
    public async Task Cancellable_valuetask_match_forwards_the_token_to_the_taken_branch()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken observed = default;

        await Err().Match(
            (_, _) => ValueTask.FromResult(0),
            (_, token) => { observed = token; return ValueTask.FromResult(-1); },
            cts.Token);

        Assert.Equal(cts.Token, observed);
    }

    // ----------------------------------------------------------- composition

    [Fact]
    public void Operators_compose_into_a_single_pipeline()
    {
        var result = Ok(4)
            .Map(value => value + 1)
            .Ensure(value => value % 2 != 0, () => new Failure("even"))
            .Bind(value => Result<string, Failure>.Success($"#{value}"));

        Assert.Equal("#5", result.Match(value => value, _ => "none"));
    }

    [Fact]
    public void A_failure_anywhere_in_a_pipeline_reaches_the_end_unchanged()
    {
        var result = Ok(4)
            .Map(value => value + 1)
            .Ensure(value => value % 2 == 0, () => new Failure("odd"))
            .Bind(value => Result<string, Failure>.Success($"#{value}"));

        Assert.Equal("odd", result.Match(_ => "none", error => error.Code));
    }
}
