using System.Net;
using TrustedIpNetwork = System.Net.IPNetwork;

namespace TestApp.Api;

public sealed record DatabaseRuntimeOptions(string ConnectionString, bool ApplyMigrationsOnStartup);
public sealed record KeycloakRuntimeOptions(string Authority, string Audience, bool RequireHttpsMetadata);

public sealed record RabbitMqRuntimeOptions(
    bool Enabled,
    string ConnectionString,
    string Exchange,
    string RoutingKeyPrefix,
    string ClientProvidedName,
    int BatchSize,
    int MaxAttempts,
    int PollIntervalSeconds,
    int BaseRetryDelaySeconds,
    int MaxRetryDelaySeconds,
    int AdvisoryLockTimeoutSeconds);

public sealed record AttemptExpirationRuntimeOptions(int BatchSize, int PollIntervalSeconds);
public sealed record RateLimitRule(int PermitLimit, int WindowSeconds, int QueueLimit);

public sealed record RateLimitingRuntimeOptions(
    RateLimitRule General,
    RateLimitRule StudentWrite,
    RateLimitRule PrivilegedRead,
    RateLimitRule Operations);

public sealed record OpenApiRuntimeOptions(bool Enabled, bool AllowAnonymous);
public sealed record CorsRuntimeOptions(bool Enabled, IReadOnlyList<string> AllowedOrigins, bool AllowCredentials);

public sealed record TransportSecurityRuntimeOptions(
    bool HttpsRedirectionEnabled,
    bool HstsEnabled,
    int HstsMaxAgeDays,
    bool HstsIncludeSubDomains,
    bool HstsPreload,
    bool SecurityHeadersEnabled);

public sealed record ReverseProxyRuntimeOptions(
    bool Enabled,
    int ForwardLimit,
    IReadOnlyList<IPAddress> KnownProxies,
    IReadOnlyList<TrustedIpNetwork> KnownNetworks);
