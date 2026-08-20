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
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadDatabase(BuildConfiguration(), Environment(Environments.Production)));

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
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadRabbitMq(BuildConfiguration(("RabbitMq:Enabled", "true"))));

        Assert.Contains("RabbitMq:ConnectionString", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Attempt_expiration_is_configuration_driven_and_validated()
    {
        var options = RuntimeConfiguration.LoadAttemptExpiration(BuildConfiguration(
            ("AttemptExpiration:BatchSize", "250"),
            ("AttemptExpiration:PollIntervalSeconds", "12")));

        Assert.Equal(250, options.BatchSize);
        Assert.Equal(12, options.PollIntervalSeconds);
    }

    [Fact]
    public void Rate_limit_classes_are_configuration_driven()
    {
        var configuration = BuildConfiguration(
            ("RateLimiting:General:PermitLimit", "100"),
            ("RateLimiting:StudentWrite:PermitLimit", "7"),
            ("RateLimiting:StudentWrite:WindowSeconds", "15"),
            ("RateLimiting:StudentWrite:QueueLimit", "2"),
            ("RateLimiting:Operations:PermitLimit", "3"));

        var options = RuntimeConfiguration.LoadRateLimiting(configuration);

        Assert.Equal(100, options.General.PermitLimit);
        Assert.Equal(7, options.StudentWrite.PermitLimit);
        Assert.Equal(15, options.StudentWrite.WindowSeconds);
        Assert.Equal(2, options.StudentWrite.QueueLimit);
        Assert.Equal(3, options.Operations.PermitLimit);
    }

    [Fact]
    public void Reverse_proxy_cannot_be_enabled_without_explicit_trust_boundary()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadReverseProxy(BuildConfiguration(("ReverseProxy:Enabled", "true"))));

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

    /// <summary>
    /// Контур за роутером имеет две переприсадки перед приложением, и обе объявляются
    /// конфигурацией: `compose.staging.yaml` отдаёт вторую доверенную сеть из
    /// `TESTAPP_UPSTREAM_PROXY_CIDR`.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Значение по умолчанию — `127.0.0.1/32`, а не пустая строка, и это не косметика.
    /// Пустое значение переменной осталось бы в конфигурации пустым элементом массива и
    /// уронило бы приложение при старте разбором CIDR. Тест удерживает оба свойства: две
    /// сети принимаются, пустая строка отвергается с внятным сообщением.
    /// </para>
    /// </remarks>
    [Fact]
    public void Reverse_proxy_accepts_a_second_trusted_network_and_rejects_an_empty_one()
    {
        var chained = RuntimeConfiguration.LoadReverseProxy(BuildConfiguration(
            ("ReverseProxy:Enabled", "true"),
            ("ReverseProxy:ForwardLimit", "2"),
            ("ReverseProxy:KnownNetworks:0", "172.20.0.0/16"),
            ("ReverseProxy:KnownNetworks:1", "192.168.3.1/32")));

        Assert.Equal(2, chained.ForwardLimit);
        Assert.Equal(2, chained.KnownNetworks.Count);

        // Умолчание из compose обязано разбираться: иначе контур без второй переприсадки
        // не поднимется вовсе.
        var single = RuntimeConfiguration.LoadReverseProxy(BuildConfiguration(
            ("ReverseProxy:Enabled", "true"),
            ("ReverseProxy:KnownNetworks:0", "172.20.0.0/16"),
            ("ReverseProxy:KnownNetworks:1", "127.0.0.1/32")));

        Assert.Equal(1, single.ForwardLimit);
        Assert.Equal(2, single.KnownNetworks.Count);

        var empty = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadReverseProxy(BuildConfiguration(
                ("ReverseProxy:Enabled", "true"),
                ("ReverseProxy:KnownNetworks:0", "172.20.0.0/16"),
                ("ReverseProxy:KnownNetworks:1", ""))));

        Assert.Contains("KnownNetworks", empty.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cors_requires_explicit_non_wildcard_origin_allow_list()
    {
        var missing = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadCors(BuildConfiguration(("Cors:Enabled", "true"))));
        Assert.Contains("AllowedOrigins", missing.Message, StringComparison.Ordinal);

        var wildcard = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadCors(BuildConfiguration(
                ("Cors:Enabled", "true"),
                ("Cors:AllowedOrigins:0", "*"))));
        Assert.Contains("Wildcard", wildcard.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cors_accepts_explicit_origin_and_credentials_setting()
    {
        var options = RuntimeConfiguration.LoadCors(BuildConfiguration(
            ("Cors:Enabled", "true"),
            ("Cors:AllowCredentials", "true"),
            ("Cors:AllowedOrigins:0", "https://frontend.example/")));

        Assert.True(options.Enabled);
        Assert.True(options.AllowCredentials);
        Assert.Equal("https://frontend.example", Assert.Single(options.AllowedOrigins));
    }

    [Fact]
    public void Production_transport_security_must_be_explicit()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeConfiguration.LoadTransportSecurity(BuildConfiguration(), Environment(Environments.Production)));

        Assert.Contains("HttpsRedirectionEnabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_transport_security_can_explicitly_delegate_tls_to_ingress()
    {
        var options = RuntimeConfiguration.LoadTransportSecurity(BuildConfiguration(
            ("TransportSecurity:HttpsRedirectionEnabled", "false"),
            ("TransportSecurity:HstsEnabled", "false"),
            ("TransportSecurity:SecurityHeadersEnabled", "true")), Environment(Environments.Production));

        Assert.False(options.HttpsRedirectionEnabled);
        Assert.False(options.HstsEnabled);
        Assert.True(options.SecurityHeadersEnabled);
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

    private static IHostEnvironment Environment(string name) => new TestHostEnvironment { EnvironmentName = name };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "TestApp.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
