using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TestApp.Application.Abstractions;
using TestApp.Domain.Identity;

namespace TestApp.Infrastructure.Identity;

public sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No active HTTP principal.");

    public ExternalUserId UserId
    {
        get
        {
            var subject = Principal.FindFirstValue("sub")
                ?? Principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new UnauthorizedAccessException("The access token has no subject claim.");
            return ExternalUserId.FromSubject(subject);
        }
    }

    public IReadOnlySet<ExternalGroupId> Groups
    {
        get
        {
            var values = Principal.FindAll("groups").SelectMany(c => Expand(c.Value));
            return values.Select(ExternalGroupId.FromExternalId).ToHashSet();
        }
    }

    private static IEnumerable<string> Expand(string value)
    {
        if (value.StartsWith('['))
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(value) ?? []; }
            catch (System.Text.Json.JsonException) { return []; }
        }
        return [value];
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
