using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TestApp.Application.Abstractions;
using TestApp.Domain.Identity;

namespace TestApp.Infrastructure.Identity;

public sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User ?? throw new InvalidOperationException("No active HTTP principal.");
    public ExternalUserId UserId => ExternalUserId.FromSubject(Principal.FindFirstValue("sub") ?? Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("The access token has no subject claim."));
    public IReadOnlySet<ExternalGroupId> Groups => Principal.FindAll("groups").SelectMany(c => Expand(c.Value)).Select(ExternalGroupId.FromExternalId).ToHashSet();
    public IReadOnlySet<string> Roles => Principal.FindAll(ClaimTypes.Role).Select(x => x.Value).Concat(Principal.FindAll("roles").SelectMany(x => Expand(x.Value))).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> Expand(string value)
    {
        if (!value.StartsWith('[')) return [value];
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(value) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }
}

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
