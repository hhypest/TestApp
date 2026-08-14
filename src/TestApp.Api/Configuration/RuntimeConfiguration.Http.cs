using System.Net;
using TrustedIpNetwork = System.Net.IPNetwork;

namespace TestApp.Api;

public static partial class RuntimeConfiguration
{
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
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (enabled && origins.Length == 0)
            throw new InvalidOperationException("Cors:Enabled=true requires at least one Cors:AllowedOrigins entry.");
        foreach (var origin in origins)
        {
            if (origin == "*")
                throw new InvalidOperationException("Wildcard CORS origins are not allowed.");
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException($"CORS origin '{origin}' must be an absolute HTTP(S) origin.");
            if (!string.IsNullOrEmpty(uri.PathAndQuery.Trim('/')) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException($"CORS origin '{origin}' must not contain a path, query or fragment.");
        }

        return new CorsRuntimeOptions(enabled, origins, allowCredentials);
    }

    public static TransportSecurityRuntimeOptions LoadTransportSecurity(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var redirect = configuration.GetValue<bool?>("TransportSecurity:HttpsRedirectionEnabled");
        var hsts = configuration.GetValue<bool?>("TransportSecurity:HstsEnabled");
        if (!environment.IsDevelopment() && redirect is null)
            throw new InvalidOperationException(
                "TransportSecurity:HttpsRedirectionEnabled must be explicitly configured outside Development.");
        if (!environment.IsDevelopment() && hsts is null)
            throw new InvalidOperationException(
                "TransportSecurity:HstsEnabled must be explicitly configured outside Development.");

        var maxAgeDays = configuration.GetValue<int?>("TransportSecurity:HstsMaxAgeDays") ?? 365;
        if (maxAgeDays < 1)
            throw new InvalidOperationException("TransportSecurity:HstsMaxAgeDays must be greater than zero.");

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
        if (forwardLimit < 1)
            throw new InvalidOperationException("ReverseProxy:ForwardLimit must be greater than zero.");

        var knownProxies = knownProxyValues.Select(ParseIpAddress).ToArray();
        var knownNetworks = knownNetworkValues.Select(ParseNetwork).ToArray();
        if (enabled && knownProxies.Length == 0 && knownNetworks.Length == 0)
            throw new InvalidOperationException(
                "ReverseProxy:Enabled=true requires at least one trusted ReverseProxy:KnownProxies or ReverseProxy:KnownNetworks entry.");
        return new ReverseProxyRuntimeOptions(enabled, forwardLimit, knownProxies, knownNetworks);
    }

    private static RateLimitRule LoadRateLimitRule(
        IConfiguration configuration,
        string name,
        int defaultPermit,
        int defaultWindow)
    {
        var prefix = $"RateLimiting:{name}";
        var permit = configuration.GetValue<int?>($"{prefix}:PermitLimit") ?? defaultPermit;
        var window = configuration.GetValue<int?>($"{prefix}:WindowSeconds") ?? defaultWindow;
        var queue = configuration.GetValue<int?>($"{prefix}:QueueLimit") ?? 0;
        if (permit < 1)
            throw new InvalidOperationException($"{prefix}:PermitLimit must be greater than zero.");
        if (window < 1)
            throw new InvalidOperationException($"{prefix}:WindowSeconds must be greater than zero.");
        if (queue < 0)
            throw new InvalidOperationException($"{prefix}:QueueLimit cannot be negative.");
        return new RateLimitRule(permit, window, queue);
    }

    private static IPAddress ParseIpAddress(string value)
    {
        if (!IPAddress.TryParse(value, out var address))
            throw new InvalidOperationException(
                $"ReverseProxy:KnownProxies contains invalid IP address '{value}'.");
        return address;
    }

    private static TrustedIpNetwork ParseNetwork(string value)
    {
        try
        {
            return TrustedIpNetwork.Parse(value);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"ReverseProxy:KnownNetworks contains invalid CIDR '{value}'.",
                ex);
        }
    }
}
