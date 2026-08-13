using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
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

public static class RuntimeConfiguration
{
    private const string DevelopmentDatabase =
        "Host=localhost;Port=5432;Database=testapp;Username=testapp;Password=testapp;";

    public static DatabaseRuntimeOptions LoadDatabase(IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException(
                    "ConnectionStrings:Database is required outside Development. Production database credentials must be supplied explicitly.");
            connectionString = DevelopmentDatabase;
        }

        var applyMigrations = configuration.GetValue<bool?>("Database:ApplyMigrationsOnStartup")
            ?? environment.IsDevelopment();
        if (!environment.IsDevelopment() && applyMigrations)
            throw new InvalidOperationException(
                "Database:ApplyMigrationsOnStartup=true is not allowed outside Development. Use the --migrate release/init job instead.");

        return new DatabaseRuntimeOptions(connectionString, applyMigrations);
    }

    public static KeycloakRuntimeOptions LoadKeycloak(IConfiguration configuration, IHostEnvironment environment)
    {
        var authority = Require(configuration["Keycloak:Authority"], "Keycloak:Authority");
        var audience = Require(configuration["Keycloak:Audience"], "Keycloak:Audience");
        var requireHttpsMetadata = configuration.GetValue<bool?>("Keycloak:RequireHttpsMetadata")
            ?? !environment.IsDevelopment();

        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
            (authorityUri.Scheme != Uri.UriSchemeHttp && authorityUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Keycloak:Authority must be an absolute HTTP(S) URI.");
        if (requireHttpsMetadata && authorityUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Keycloak:Authority must use HTTPS when Keycloak:RequireHttpsMetadata=true.");

        return new KeycloakRuntimeOptions(authority.TrimEnd('/'), audience, requireHttpsMetadata);
    }

    public static RabbitMqRuntimeOptions LoadRabbitMq(IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool>("RabbitMq:Enabled");
        var connectionString = configuration["RabbitMq:ConnectionString"]?.Trim() ?? string.Empty;
        var exchange = configuration["RabbitMq:Exchange"]?.Trim() ?? "testapp.events";
        var routingKeyPrefix = configuration["RabbitMq:RoutingKeyPrefix"]?.Trim() ?? "testapp";
        var clientProvidedName = configuration["RabbitMq:ClientProvidedName"]?.Trim() ?? "TestApp.Outbox";
        var batchSize = configuration.GetValue<int?>("Outbox:BatchSize") ?? 100;
        var maxAttempts = configuration.GetValue<int?>("Outbox:MaxAttempts") ?? 10;
        var pollIntervalSeconds = configuration.GetValue<int?>("Outbox:PollIntervalSeconds") ?? 5;
        var baseRetryDelaySeconds = configuration.GetValue<int?>("Outbox:BaseRetryDelaySeconds") ?? 5;
        var maxRetryDelaySeconds = configuration.GetValue<int?>("Outbox:MaxRetryDelaySeconds") ?? 900;
        var advisoryLockTimeoutSeconds = configuration.GetValue<int?>("Outbox:AdvisoryLockTimeoutSeconds") ?? 5;

        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("RabbitMq:ConnectionString is required when RabbitMq:Enabled=true.");
            if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "amqp" && uri.Scheme != "amqps"))
                throw new InvalidOperationException("RabbitMq:ConnectionString must be an absolute amqp:// or amqps:// URI.");
            if (string.IsNullOrWhiteSpace(exchange))
                throw new InvalidOperationException("RabbitMq:Exchange cannot be empty when RabbitMQ delivery is enabled.");
            if (string.IsNullOrWhiteSpace(routingKeyPrefix))
                throw new InvalidOperationException("RabbitMq:RoutingKeyPrefix cannot be empty when RabbitMQ delivery is enabled.");
        }

        if (batchSize is < 1 or > 1000) throw new InvalidOperationException("Outbox:BatchSize must be between 1 and 1000.");
        if (maxAttempts < 1) throw new InvalidOperationException("Outbox:MaxAttempts must be greater than zero.");
        if (pollIntervalSeconds < 1) throw new InvalidOperationException("Outbox:PollIntervalSeconds must be greater than zero.");
        if (baseRetryDelaySeconds < 1) throw new InvalidOperationException("Outbox:BaseRetryDelaySeconds must be greater than zero.");
        if (maxRetryDelaySeconds < baseRetryDelaySeconds)
            throw new InvalidOperationException("Outbox:MaxRetryDelaySeconds cannot be smaller than Outbox:BaseRetryDelaySeconds.");
        if (advisoryLockTimeoutSeconds < 0)
            throw new InvalidOperationException("Outbox:AdvisoryLockTimeoutSeconds cannot be negative.");

        return new RabbitMqRuntimeOptions(enabled, connectionString, exchange, routingKeyPrefix, clientProvidedName,
            batchSize, maxAttempts, pollIntervalSeconds, baseRetryDelaySeconds, maxRetryDelaySeconds, advisoryLockTimeoutSeconds);
    }

    public static AttemptExpirationRuntimeOptions LoadAttemptExpiration(IConfiguration configuration)
    {
        var batchSize = configuration.GetValue<int?>("AttemptExpiration:BatchSize") ?? 100;
        var pollIntervalSeconds = configuration.GetValue<int?>("AttemptExpiration:PollIntervalSeconds") ?? 30;
        if (batchSize is < 1 or > 1000)
            throw new InvalidOperationException("AttemptExpiration:BatchSize must be between 1 and 1000.");
        if (pollIntervalSeconds < 1)
            throw new InvalidOperationException("AttemptExpiration:PollIntervalSeconds must be greater than zero.");
        return new AttemptExpirationRuntimeOptions(batchSize, pollIntervalSeconds);
    }

    public static RateLimitingRuntimeOptions LoadRateLimiting(IConfiguration configuration) => new(
        LoadRateLimitRule(configuration, "General", 120, 60),
        LoadRateLimitRule(configuration, "StudentWrite", 60, 60),
        LoadRateLimitRule(configuration, "PrivilegedRead", 60, 60),
        LoadRateLimitRule(configuration, "Operations", 30, 60));

    public static OpenApiRuntimeOptions LoadOpenApi(IConfiguration configuration, IHostEnvironment environment)
    {
        var enabled = configuration.GetValue<bool?>("OpenApi:Enabled") ?? environment.IsDevelopment();
        var allowAnonymous = configuration.GetValue<bool?>("OpenApi:AllowAnonymous") ?? environment.IsDevelopment();
        return new OpenApiRuntimeOptions(enabled, enabled && allowAnonymous);
    }

    public static CorsRuntimeOptions LoadCors(IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool>("Cors:Enabled");
        var allowCredentials = configuration.GetValue<bool>("Cors:AllowCredentials");
        var origins = (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .Select(x => x.Trim().TrimEnd('/'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (enabled && origins.Length == 0)
            throw new InvalidOperationException("Cors:Enabled=true requires at least one Cors:AllowedOrigins entry.");
        foreach (var origin in origins)
        {
            if (origin == "*") throw new InvalidOperationException("Wildcard CORS origins are not allowed.");
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException($"CORS origin '{origin}' must be an absolute HTTP(S) origin.");
            if (!string.IsNullOrEmpty(uri.PathAndQuery.Trim('/')) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException($"CORS origin '{origin}' must not contain a path, query or fragment.");
        }

        return new CorsRuntimeOptions(enabled, origins, allowCredentials);
    }

    public static TransportSecurityRuntimeOptions LoadTransportSecurity(IConfiguration configuration, IHostEnvironment environment)
    {
        var redirect = configuration.GetValue<bool?>("TransportSecurity:HttpsRedirectionEnabled");
        var hsts = configuration.GetValue<bool?>("TransportSecurity:HstsEnabled");
        if (!environment.IsDevelopment() && redirect is null)
            throw new InvalidOperationException("TransportSecurity:HttpsRedirectionEnabled must be explicitly configured outside Development.");
        if (!environment.IsDevelopment() && hsts is null)
            throw new InvalidOperationException("TransportSecurity:HstsEnabled must be explicitly configured outside Development.");

        var maxAgeDays = configuration.GetValue<int?>("TransportSecurity:HstsMaxAgeDays") ?? 365;
        if (maxAgeDays < 1) throw new InvalidOperationException("TransportSecurity:HstsMaxAgeDays must be greater than zero.");

        return new TransportSecurityRuntimeOptions(
            redirect ?? false,
            hsts ?? false,
            maxAgeDays,
            configuration.GetValue<bool?>("TransportSecurity:HstsIncludeSubDomains") ?? true,
            configuration.GetValue<bool?>("TransportSecurity:HstsPreload") ?? false,
            configuration.GetValue<bool?>("TransportSecurity:SecurityHeadersEnabled") ?? !environment.IsDevelopment());
    }

    public static ReverseProxyRuntimeOptions LoadReverseProxy(IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool>("ReverseProxy:Enabled");
        var forwardLimit = configuration.GetValue<int?>("ReverseProxy:ForwardLimit") ?? 1;
        var knownProxyValues = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        var knownNetworkValues = configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
        if (forwardLimit < 1) throw new InvalidOperationException("ReverseProxy:ForwardLimit must be greater than zero.");

        var knownProxies = knownProxyValues.Select(ParseIpAddress).ToArray();
        var knownNetworks = knownNetworkValues.Select(ParseNetwork).ToArray();
        if (enabled && knownProxies.Length == 0 && knownNetworks.Length == 0)
            throw new InvalidOperationException(
                "ReverseProxy:Enabled=true requires at least one trusted ReverseProxy:KnownProxies or ReverseProxy:KnownNetworks entry.");
        return new ReverseProxyRuntimeOptions(enabled, forwardLimit, knownProxies, knownNetworks);
    }

    public static void RegisterTypedOptions(
        IServiceCollection services,
        DatabaseRuntimeOptions database,
        KeycloakRuntimeOptions? keycloak,
        RabbitMqRuntimeOptions rabbitMq,
        AttemptExpirationRuntimeOptions expiration,
        RateLimitingRuntimeOptions rateLimiting,
        OpenApiRuntimeOptions openApi,
        CorsRuntimeOptions cors,
        TransportSecurityRuntimeOptions? transportSecurity,
        ReverseProxyRuntimeOptions reverseProxy)
    {
        services.AddSingleton(Options.Create(database));
        if (keycloak is not null) services.AddSingleton(Options.Create(keycloak));
        services.AddSingleton(Options.Create(rabbitMq));
        services.AddSingleton(Options.Create(expiration));
        services.AddSingleton(Options.Create(rateLimiting));
        services.AddSingleton(Options.Create(openApi));
        services.AddSingleton(Options.Create(cors));
        if (transportSecurity is not null) services.AddSingleton(Options.Create(transportSecurity));
        services.AddSingleton(Options.Create(reverseProxy));
    }

    public static void ConfigureForwardedHeaders(IServiceCollection services, ReverseProxyRuntimeOptions reverseProxy)
    {
        if (!reverseProxy.Enabled) return;
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = reverseProxy.ForwardLimit;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in reverseProxy.KnownProxies) options.KnownProxies.Add(proxy);
            foreach (var network in reverseProxy.KnownNetworks) options.KnownIPNetworks.Add(network);
        });
    }

    private static RateLimitRule LoadRateLimitRule(IConfiguration configuration, string name, int defaultPermit, int defaultWindow)
    {
        var prefix = $"RateLimiting:{name}";
        var permit = configuration.GetValue<int?>($"{prefix}:PermitLimit") ?? defaultPermit;
        var window = configuration.GetValue<int?>($"{prefix}:WindowSeconds") ?? defaultWindow;
        var queue = configuration.GetValue<int?>($"{prefix}:QueueLimit") ?? 0;
        if (permit < 1) throw new InvalidOperationException($"{prefix}:PermitLimit must be greater than zero.");
        if (window < 1) throw new InvalidOperationException($"{prefix}:WindowSeconds must be greater than zero.");
        if (queue < 0) throw new InvalidOperationException($"{prefix}:QueueLimit cannot be negative.");
        return new RateLimitRule(permit, window, queue);
    }

    private static string Require(string? value, string key) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new InvalidOperationException($"{key} is required for the HTTP API host.");

    private static IPAddress ParseIpAddress(string value)
    {
        if (!IPAddress.TryParse(value, out var address))
            throw new InvalidOperationException($"ReverseProxy:KnownProxies contains invalid IP address '{value}'.");
        return address;
    }

    private static TrustedIpNetwork ParseNetwork(string value)
    {
        try { return TrustedIpNetwork.Parse(value); }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"ReverseProxy:KnownNetworks contains invalid CIDR '{value}'.", ex);
        }
    }
}
