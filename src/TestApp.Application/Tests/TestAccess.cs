using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Domain.Identity;
using TestApp.Domain.Tests;

namespace TestApp.Application.Tests;

internal static class TestAccess
{
    private const string AdminRole = "test-admin";

    public static Error? EnsureCanManage(Test test, ICurrentActor actor) =>
        IsAdmin(actor) || test.IsOwnedBy(actor.UserId)
            ? null
            : Error.Forbidden("test.forbidden", "The current user is not allowed to manage this test.");

    public static ExternalUserId? OwnerFilter(ICurrentActor actor) =>
        IsAdmin(actor) ? null : actor.UserId;

    public static bool IsAdmin(ICurrentActor actor) =>
        actor.Roles.Any(role => string.Equals(role, AdminRole, StringComparison.OrdinalIgnoreCase));
}
