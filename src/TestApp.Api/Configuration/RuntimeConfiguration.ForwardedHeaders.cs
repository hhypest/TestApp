using Microsoft.AspNetCore.HttpOverrides;

namespace TestApp.Api;

public static partial class RuntimeConfiguration
{
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
