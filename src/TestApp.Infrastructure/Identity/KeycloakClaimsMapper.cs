using System.Security.Claims;
using System.Text.Json;
using TestApp.Domain.Identity;

namespace TestApp.Infrastructure.Identity;

public sealed record KeycloakIdentity(
    ExternalUserId UserId,
    IReadOnlySet<ExternalGroupId> Groups,
    IReadOnlySet<string> Roles);

public static class KeycloakClaimsMapper
{
    public static KeycloakIdentity Map(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subject = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("The access token has no subject claim.");

        var groups = principal.FindAll("groups")
            .SelectMany(c => Expand(c.Value))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(ExternalGroupId.FromExternalId)
            .ToHashSet();

        var roles = principal.FindAll(ClaimTypes.Role)
            .Select(x => x.Value)
            .Concat(principal.FindAll("roles").SelectMany(x => Expand(x.Value)))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new KeycloakIdentity(ExternalUserId.FromSubject(subject), groups, roles);
    }

    private static IEnumerable<string> Expand(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        if (!value.TrimStart().StartsWith('[')) return [value];
        try { return JsonSerializer.Deserialize<string[]>(value) ?? []; }
        catch (JsonException) { return []; }
    }
}
