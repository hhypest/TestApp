using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace TestApp.Api;

public static partial class RuntimeConfiguration
{
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
        if (keycloak is not null)
            services.AddSingleton(Options.Create(keycloak));
        services.AddSingleton(Options.Create(rabbitMq));
        services.AddSingleton(Options.Create(expiration));
        services.AddSingleton(Options.Create(rateLimiting));
        services.AddSingleton(Options.Create(openApi));
        services.AddSingleton(Options.Create(cors));
        if (transportSecurity is not null)
            services.AddSingleton(Options.Create(transportSecurity));
        services.AddSingleton(Options.Create(reverseProxy));
    }

    public static void ConfigureForwardedHeaders(
        IServiceCollection services,
        ReverseProxyRuntimeOptions reverseProxy)
    {
        if (!reverseProxy.Enabled)
            return;

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto |
                ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = reverseProxy.ForwardLimit;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in reverseProxy.KnownProxies)
                options.KnownProxies.Add(proxy);
            foreach (var network in reverseProxy.KnownNetworks)
                options.KnownIPNetworks.Add(network);
        });
    }
}
