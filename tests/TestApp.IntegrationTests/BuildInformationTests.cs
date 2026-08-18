using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Xunit;
using TestApp.Api;

namespace TestApp.IntegrationTests;

/// <summary>
/// «Что именно развёрнуто» (issue #17): единый источник версии и её видимость на контуре.
/// </summary>
/// <remarks>
/// До появления `Directory.Build.props` версии не было нигде — сборки получали неявный `1.0.0`,
/// поэтому по развёрнутому образу нельзя было сказать, что в нём. Гейт §7 пункт 10 требует
/// runbook отката на конкретный digest; без ответа «какая версия сейчас» откат выполняется вслепую.
/// </remarks>
public sealed class BuildInformationTests
{
    [Fact]
    public void The_version_comes_from_the_single_source_and_is_never_unknown()
    {
        var build = BuildInformation.Create(typeof(BuildInformation).Assembly);

        Assert.NotEqual("unknown", build.Version);
        Assert.StartsWith("1.0.0", build.Version, StringComparison.Ordinal);
        // Build metadata после '+' (хеш коммита от SourceLink) не должен утекать в операционный ответ.
        Assert.DoesNotContain("+", build.Version, StringComparison.Ordinal);
    }

    /// <summary>
    /// Digest приходит из окружения, а не из сборки: один и тот же образ разворачивается под разными
    /// тегами, и только digest отвечает на вопрос «что именно запущено».
    /// </summary>
    [Fact]
    public void The_image_digest_is_read_from_the_deployment_variable()
    {
        var original = Environment.GetEnvironmentVariable(BuildInformation.ImageDigestVariable);
        try
        {
            Environment.SetEnvironmentVariable(BuildInformation.ImageDigestVariable, "  sha256:abc123  ");
            Assert.Equal("sha256:abc123", BuildInformation.Create(typeof(BuildInformation).Assembly).ImageDigest);

            Environment.SetEnvironmentVariable(BuildInformation.ImageDigestVariable, "   ");
            Assert.Null(BuildInformation.Create(typeof(BuildInformation).Assembly).ImageDigest);

            Environment.SetEnvironmentVariable(BuildInformation.ImageDigestVariable, null);
            Assert.Null(BuildInformation.Create(typeof(BuildInformation).Assembly).ImageDigest);
        }
        finally
        {
            Environment.SetEnvironmentVariable(BuildInformation.ImageDigestVariable, original);
        }
    }

    /// <summary>
    /// Версия должна приходить из `Directory.Build.props`, а не из неявного умолчания SDK.
    /// Отличить одно от другого по самому числу нельзя, поэтому проверяется наличие
    /// информационной версии — её SDK без явного `VersionPrefix` не проставляет осмысленно.
    /// </summary>
    [Fact]
    public void The_assembly_carries_the_version_from_the_shared_props_file()
    {
        Assert.Equal("1.0.0", typeof(BuildInformation).Assembly.GetName().Version?.ToString(3));

        var informational = typeof(BuildInformation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(informational));
    }

    [Fact]
    public async Task The_version_endpoint_reports_the_running_build_to_operators_only()
    {
        // Эндпоинт версии к базе не обращается, поэтому тест не поднимает PostgreSQL:
        // проверка «что развёрнуто» должна работать и там, где Docker недоступен.
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ApiTestHost.Create(
            "Host=127.0.0.1;Port=1;Database=version-probe;Username=none;Password=none",
            builder => builder.UseSetting("Database:ApplyMigrationsOnStartup", "false"));

        using var client = factory.CreateClient();

        ApiTestHost.Authenticate(client, "student-version", "student");
        using (var forbidden = await client.GetAsync("/api/v1/operations/version", ct))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        ApiTestHost.Authenticate(client, "admin-version", "test-admin");
        using var response = await client.GetAsync("/api/v1/operations/version", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var build = await response.Content.ReadFromJsonAsync<BuildInformation>(cancellationToken: ct);
        Assert.NotNull(build);
        Assert.StartsWith("1.0.0", build.Version, StringComparison.Ordinal);
    }
}
