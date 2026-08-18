using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TestApp.IntegrationTests;

/// <summary>
/// Заморозка публичного контракта v1 (issue #22, гейт `docs/ROADMAP.md` §7 пункт 1).
/// </summary>
/// <remarks>
/// <para>
/// <c>OpenApiContractTests</c> проверяет отдельные свойства документа, которые мы решили считать
/// обязательными. Этот тест решает другую задачу: он ловит <em>любое</em> изменение формы документа,
/// включая то, о котором никто не подумал заранее — новый эндпоинт, исчезнувший код ответа,
/// переименованное поле DTO. Снимок в <c>docs/openapi/v1.json</c> — артефакт версии, а не кэш:
/// его расхождение с живым документом означает, что публичный контракт изменился, и это изменение
/// должно быть осознанным.
/// </para>
/// <para>
/// Тест намеренно не требует PostgreSQL: эндпоинт OpenAPI не обращается к базе, поэтому хост
/// поднимается с заведомо недоступной строкой подключения и выключенными startup-миграциями.
/// Благодаря этому заморозка контракта проверяется и там, где Docker недоступен (см. ADR-032).
/// </para>
/// <para>
/// Обновление снимка — осознанное действие, а не побочный эффект прогона:
/// <c>UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshotTests</c>.
/// Полученный diff обязан попасть в тот же change set, что и правка контракта.
/// </para>
/// </remarks>
public sealed class OpenApiSnapshotTests
{
    private const string SnapshotRelativePath = "docs/openapi/v1.json";
    private const string UpdateVariable = "UPDATE_OPENAPI_SNAPSHOT";

    [Fact]
    public async Task The_published_contract_matches_the_committed_v1_snapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var live = Normalize(await FetchDocumentAsync(ct));
        var snapshotPath = Path.Combine(RepositoryRoot(), SnapshotRelativePath);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllTextAsync(snapshotPath, live, ct);
            return;
        }

        Assert.True(
            File.Exists(snapshotPath),
            $"Снимок контракта v1 отсутствует: {SnapshotRelativePath}. " +
            $"Создать: {UpdateVariable}=1 dotnet test --filter OpenApiSnapshotTests");

        var snapshot = Normalize(await File.ReadAllTextAsync(snapshotPath, ct));
        if (string.Equals(snapshot, live, StringComparison.Ordinal))
            return;

        throw new Xunit.Sdk.XunitException(
            "Публичный контракт v1 разошёлся с замороженным снимком." + Environment.NewLine +
            Describe(snapshot, live) + Environment.NewLine +
            "Если изменение намеренное — обновить снимок в том же change set:" + Environment.NewLine +
            $"  {UpdateVariable}=1 dotnet test --filter OpenApiSnapshotTests" + Environment.NewLine +
            "и описать изменение по политике совместимости `docs/API.md` §1.");
    }

    /// <summary>
    /// Снимок бесполезен, если его нельзя прочитать как JSON: тогда diff покажет мусор,
    /// а не изменение контракта.
    /// </summary>
    [Fact]
    public async Task The_committed_snapshot_is_a_readable_openapi_document()
    {
        var ct = TestContext.Current.CancellationToken;
        var snapshotPath = Path.Combine(RepositoryRoot(), SnapshotRelativePath);
        Assert.True(File.Exists(snapshotPath), $"Снимок контракта v1 отсутствует: {SnapshotRelativePath}");

        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(snapshotPath, ct));
        var root = document.RootElement;

        Assert.StartsWith("3.", root.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.True(root.GetProperty("paths").EnumerateObject().Any());
        Assert.True(root.GetProperty("components").GetProperty("schemas").EnumerateObject().Any());
    }

    private static async Task<string> FetchDocumentAsync(CancellationToken ct)
    {
        await using var factory = ApiTestHost.Create(
            "Host=127.0.0.1;Port=1;Database=contract-freeze;Username=none;Password=none",
            builder => builder.UseSetting("Database:ApplyMigrationsOnStartup", "false"));

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TestApp.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new Xunit.Sdk.XunitException("Не найден корень репозитория (TestApp.slnx) вверх от каталога сборки.");
    }

    /// <summary>Первое расхождение с контекстом — иначе в отчёте окажутся 400 КБ JSON.</summary>
    private static string Describe(string snapshot, string live)
    {
        var expected = snapshot.Split('\n');
        var actual = live.Split('\n');

        for (var line = 0; line < Math.Max(expected.Length, actual.Length); line++)
        {
            var expectedLine = line < expected.Length ? expected[line] : "<конец файла>";
            var actualLine = line < actual.Length ? actual[line] : "<конец файла>";
            if (string.Equals(expectedLine, actualLine, StringComparison.Ordinal))
                continue;

            return $"Первое расхождение в строке {line + 1}:" + Environment.NewLine +
                   $"  снимок: {Truncate(expectedLine)}" + Environment.NewLine +
                   $"  сейчас: {Truncate(actualLine)}" + Environment.NewLine +
                   $"Строк в снимке: {expected.Length}, в текущем документе: {actual.Length}.";
        }

        return $"Строк в снимке: {expected.Length}, в текущем документе: {actual.Length}.";
    }

    private static string Truncate(string value) =>
        value.Length <= 160 ? value.Trim() : value.Trim()[..160] + "…";
}
