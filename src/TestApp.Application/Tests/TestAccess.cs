using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Domain.Identity;
using TestApp.Domain.Tests;

namespace TestApp.Application.Tests;

internal static class TestAccess
{
    public static Error? EnsureCanManage(Test test, ICurrentActor actor) =>
        IsAdmin(actor) || test.IsOwnedBy(actor.UserId)
            ? null
            : Error.Forbidden("test.forbidden", "The current user is not allowed to manage this test.");

    public static Error? EnsureExpectedVersion(Test test, long? expectedVersion) =>
        expectedVersion is null || test.ConcurrencyVersion == expectedVersion.Value
            ? null
            : Error.PreconditionFailed(
                "concurrency.precondition_failed",
                $"The test has changed. Expected version {expectedVersion.Value}, current version {test.ConcurrencyVersion}.");

    public static ExternalUserId? OwnerFilter(ICurrentActor actor) => ActorScope.OwnerFilter(actor);

    public static bool IsAdmin(ICurrentActor actor) => ActorScope.IsAdmin(actor);
}
