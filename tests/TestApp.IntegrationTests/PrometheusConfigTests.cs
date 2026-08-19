using Xunit;

namespace TestApp.IntegrationTests;

/// <summary>
/// Конфигурация Prometheus контура обязана отличаться от локальной ровно одним — маршрутизацией
/// алертов (issue #16/#19).
/// </summary>
/// <remarks>
/// <para>
/// Контур наблюдает те же метрики и вычисляет те же правила, что локальный стенд и CI: иначе
/// пороги, откалиброванные на контуре, окажутся неприменимы, а `observability` в CI перестанет
/// быть проверкой того, что реально развёрнуто.
/// </para>
/// <para>
/// Расхождение здесь тихое: файл собирают руками, добавляя scrape job «только на контур», и
/// узнают об этом при разборе инцидента, когда нужного графика не оказывается. Тест сравнивает
/// то, что должно совпадать, и требует наличия того единственного, что отличается.
/// </para>
/// <para>
/// Разбор намеренно строковый, без YAML-библиотеки: у тестового проекта её нет, а формат
/// достаточно прост. Ложное срабатывание при переформатировании — приемлемая цена: оно заметно
/// и чинится обновлением обоих файлов, что и требуется.
/// </para>
/// </remarks>
public sealed class PrometheusConfigTests
{
    private const string LocalConfig = "deploy/prometheus/prometheus.yml";
    private const string StagingConfig = "deploy/staging/prometheus.yml";

    [Fact]
    public void The_staging_config_scrapes_and_evaluates_exactly_what_the_local_one_does()
    {
        var local = Meaningful(LocalConfig);
        var staging = Meaningful(StagingConfig);

        // Всё, что не относится к alerting, обязано совпадать по составу.
        var localWithoutAlerting = local.Where(line => !IsAlertingLine(line, local)).ToArray();
        var stagingWithoutAlerting = staging.Where(line => !IsAlertingLine(line, staging)).ToArray();

        Assert.Equal(localWithoutAlerting, stagingWithoutAlerting);
    }

    [Fact]
    public void Only_the_staging_config_routes_alerts_to_alertmanager()
    {
        var local = string.Join('\n', Meaningful(LocalConfig));
        var staging = string.Join('\n', Meaningful(StagingConfig));

        Assert.Contains("alerting:", staging, StringComparison.Ordinal);
        Assert.Contains("alertmanager:9093", staging, StringComparison.Ordinal);

        // На локальном стенде Alertmanager не поднимается: конфиг с недоступным приёмником
        // заставил бы Prometheus логировать отказ на каждом цикле.
        Assert.DoesNotContain("alerting:", local, StringComparison.Ordinal);
    }

    /// <summary>
    /// Правила ссылаются на файл по пути внутри контейнера — он обязан совпадать с точкой
    /// монтирования в обоих compose, иначе Prometheus стартует без единого правила и
    /// молчит именно тогда, когда должен сработать.
    /// </summary>
    [Fact]
    public void Both_configs_load_the_same_rule_file_that_actually_exists()
    {
        foreach (var config in new[] { LocalConfig, StagingConfig })
            Assert.Contains("/etc/prometheus/alerts.yml", string.Join('\n', Meaningful(config)), StringComparison.Ordinal);

        var rules = Path.Combine(RepositoryRoot(), "deploy/prometheus/alerts.yml");
        Assert.True(File.Exists(rules), "Файл правил, на который ссылаются оба конфига, отсутствует.");

        var content = File.ReadAllText(rules);
        Assert.Contains("severity: page", content, StringComparison.Ordinal);
        Assert.Contains("severity: warning", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Маршрутизация Alertmanager разбирает алерты по метке severity. Если правила перестанут
    /// её проставлять, всё поедет в общий приёмник с интервалом повтора для warning —
    /// то есть page-алерт будет напоминать о себе вчетверо реже, чем задумано.
    /// </summary>
    [Fact]
    public void Alertmanager_routes_the_severity_labels_the_rules_actually_emit()
    {
        var routing = File.ReadAllText(Path.Combine(RepositoryRoot(), "deploy/alertmanager/alertmanager.yml"));

        Assert.Contains("severity=\"page\"", routing, StringComparison.Ordinal);
        Assert.Contains("url_file:", routing, StringComparison.Ordinal);
        // URL webhook'а — секрет и не должен оказаться в репозитории.
        Assert.DoesNotContain("https://hooks.", routing, StringComparison.Ordinal);
    }

    private static string[] Meaningful(string relativePath) =>
        File.ReadAllLines(Path.Combine(RepositoryRoot(), relativePath))
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0 && !line.TrimStart().StartsWith('#'))
            .ToArray();

    private static bool IsAlertingLine(string line, string[] all)
    {
        var index = Array.IndexOf(all, line);
        for (var i = index; i >= 0; i--)
        {
            if (all[i].StartsWith("alerting:", StringComparison.Ordinal))
                return true;
            if (!char.IsWhiteSpace(all[i][0]) && !all[i].StartsWith('-'))
                return false;
        }

        return false;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TestApp.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new Xunit.Sdk.XunitException("Не найден корень репозитория (TestApp.slnx).");
    }
}
