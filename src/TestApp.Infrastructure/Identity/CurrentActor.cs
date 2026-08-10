using Microsoft.AspNetCore.Http;
using TestApp.Application.Abstractions;
using TestApp.Domain.Identity;

namespace TestApp.Infrastructure.Identity;

public sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    private KeycloakIdentity Identity => KeycloakClaimsMapper.Map(
        accessor.HttpContext?.User ?? throw new InvalidOperationException("No active HTTP principal."));

    public ExternalUserId UserId => Identity.UserId;
    public IReadOnlySet<ExternalGroupId> Groups => Identity.Groups;
    public IReadOnlySet<string> Roles => Identity.Roles;
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
