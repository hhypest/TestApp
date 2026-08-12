using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TestApp.Api;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class RuntimeConfigurationTests
{
    [Fact]
    public void Production_requires_explicit_database_connection_string()
    {
        var configuration = BuildConfiguration();
        var environment = Environment(Environments.Production);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadDatabase(configuration, environment));

        Assert.Contains("ConnectionStrings:Database", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_database_default_is_explicitly_development_only()
    {
        var options = RuntimeConfiguration.LoadDatabase(BuildConfiguration(), Environment(Environments.Development));

        Assert.Contains("Database=testapp", options.ConnectionString, StringComparison.Ordinal);
        Assert.True(options.ApplyMigrationsOnStartup);
    }

    [Fact]
    public void Production_rejects_automatic_schema_migration()
    {
        var configuration = BuildConfiguration(
            ("ConnectionStrings:Database", "Server=db;Database=testapp;User=testapp;Password=secret;"),
            ("Database:ApplyMigrationsOnStartup", "true"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadDatabase(configuration, Environment(Environments.Production)));

        Assert.Contains("--migrate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_keycloak_requires_https_metadata_by_default()
    {
        var configuration = BuildConfiguration(
            ("Keycloak:Authority", "http://identity.internal/realms/testapp"),
            ("Keycloak:Audience", "testapp-api"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadKeycloak(configuration, Environment(Environments.Production)));

        Assert.Contains("HTTPS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enabled_rabbitmq_requires_valid_connection_string()
    {
        var configuration = BuildConfiguration(("RabbitMq:Enabled", "true"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadRabbitMq(configuration));

        Assert.Contains("RabbitMq:ConnectionString", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rate_limit_values_are_configuration_driven_and_validated()
    {
        var configuration = BuildConfiguration(
            ("RateLimiting:PermitLimit", "7"),
            ("RateLimiting:WindowSeconds", "15"),
            ("RateLimiting:QueueLimit", "2"));

        var options = RuntimeConfiguration.LoadRateLimiting(configuration);

        Assert.Equal(7, options.PermitLimit);
        Assert.Equal(15, options.WindowSeconds);
        Assert.Equal(2, options.QueueLimit);
    }

    [Fact]
    public void Reverse_proxy_cannot_be_enabled_without_explicit_trust_boundary()
    {
        var configuration = BuildConfiguration(("ReverseProxy:Enabled", "true"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadReverseProxy(configuration));

        Assert.Contains("KnownProxies", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reverse_proxy_accepts_trusted_ip_and_cidr_configuration()
    {
        var configuration = BuildConfiguration(
            ("ReverseProxy:Enabled", "true"),
            ("ReverseProxy:ForwardLimit", "2"),
            ("ReverseProxy:KnownProxies:0", "10.0.0.10"),
            ("ReverseProxy:KnownNetworks:0", "192.168.0.0/24"));

        var options = RuntimeConfiguration.LoadReverseProxy(configuration);

        Assert.True(options.Enabled);
        Assert.Equal(2, options.ForwardLimit);
        Assert.Single(options.KnownProxies);
        Assert.Single(options.KnownNetworks);
    }

    [Fact]
    public void OpenApi_defaults_to_development_only_public_exposure()
    {
        var configuration = BuildConfiguration();

        var development = RuntimeConfiguration.LoadOpenApi(configuration, Environment(Environments.Development));
        var production = RuntimeConfiguration.LoadOpenApi(configuration, Environment(Environments.Production));

        Assert.True(development.Enabled);
        Assert.True(development.AllowAnonymous);
        Assert.False(production.Enabled);
        Assert.False(production.AllowAnonymous);
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();

    private static IHostEnvironment Environment(string name) => new TestHostEnvironment
    {
        EnvironmentName = name
    };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "TestApp.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
