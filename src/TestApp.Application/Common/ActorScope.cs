using TestApp.Application.Abstractions;
using TestApp.Domain.Identity;

namespace TestApp.Application.Common;

/// <summary>
/// Single source of truth for the role that grants global (non-owner-scoped)
/// access to tests, revisions and reviewer results.
/// </summary>
/// <remarks>
/// This decision must exist in exactly one place. It is what separates
/// "sees every author's data" from "sees only their own", so a role rename
/// applied to some copies but not others would silently widen or narrow data
/// visibility rather than fail loudly.
/// </remarks>
internal static class ActorScope
{
    private const string AdminRole = "test-admin";

    public static bool IsAdmin(ICurrentActor actor) =>
        actor.Roles.Any(role => string.Equals(role, AdminRole, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns <c>null</c> for an administrator (global scope), otherwise the
    /// current user's id, which callers apply as an owner filter.
    /// </summary>
    public static ExternalUserId? OwnerFilter(ICurrentActor actor) =>
        IsAdmin(actor) ? null : actor.UserId;
}
