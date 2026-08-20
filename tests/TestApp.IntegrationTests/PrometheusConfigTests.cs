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

    /// <summary>
    /// У контура ровно два отличия от локального стенда, и оба названы: маршрутизация алертов
    /// и внешняя проба через ingress. Всё остальное обязано совпадать.
    /// </summary>
    /// <remarks>
    /// Список отличий закрытый намеренно. Разрешить «staging может содержать что-то ещё»
    /// значило бы вернуть ту самую тихую расходимость, ради которой тест написан: конфиг
    /// собирают руками, добавляют scrape job «только на контур», и узнают об этом при разборе
    /// инцидента, когда нужного графика не оказывается.
    /// </remarks>
    [Fact]
    public void The_staging_config_differs_from_the_local_one_only_where_it_is_allowed_to()
    {
        var local = Meaningful(LocalConfig);
        var staging = Meaningful(StagingConfig);

        var localComparable = local.Where(line => !IsAlertingLine(line, local)).ToArray();
        var stagingComparable = WithoutIngressProbe(staging.Where(line => !IsAlertingLine(line, staging)))
            .Where(line => !line.Contains("alerts-ingress.yml", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(localComparable, stagingComparable);
    }

    /// <summary>
    /// Внешняя проба существует только на контуре: ingress есть только там. Локальный стенд
    /// не должен получить ни job, ни правил к нему — иначе `absent()` сработает навсегда.
    /// </summary>
    [Fact]
    public void Only_the_staging_config_probes_the_public_entry_point()
    {
        var staging = string.Join('\n', Meaningful(StagingConfig));
        var local = string.Join('\n', Meaningful(LocalConfig));

        Assert.Contains("job_name: testapp-ingress", staging, StringComparison.Ordinal);
        Assert.Contains("/etc/prometheus/targets/ingress.json", staging, StringComparison.Ordinal);
        Assert.Contains("alerts-ingress.yml", staging, StringComparison.Ordinal);

        Assert.DoesNotContain("testapp-ingress", local, StringComparison.Ordinal);

        var rules = File.ReadAllText(Path.Combine(RepositoryRoot(), "deploy/staging/alerts-ingress.yml"));
        Assert.Contains("probe_success{job=\"testapp-ingress\"}", rules, StringComparison.Ordinal);

        // Пустой job молчит так же, как исправный. Правило absent() — единственное, что
        // отличает «внешняя проба в порядке» от «внешней пробы нет».
        Assert.Contains("absent(up{job=\"testapp-ingress\"})", rules, StringComparison.Ordinal);
    }

    /// <summary>Блок job'а внешней пробы: от строки job_name до следующего job или до конца.</summary>
    private static IEnumerable<string> WithoutIngressProbe(IEnumerable<string> lines)
    {
        var skipping = false;
        foreach (var line in lines)
        {
            if (line.Trim() == "- job_name: testapp-ingress")
            {
                skipping = true;
                continue;
            }

            if (skipping)
            {
                var startsNewBlock = line.TrimStart().StartsWith("- job_name:", StringComparison.Ordinal)
                                     || !char.IsWhiteSpace(line[0]);
                if (!startsNewBlock)
                    continue;

                skipping = false;
            }

            yield return line;
        }
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

    /// <summary>
    /// Готовность проверяется активной пробой, а не метрикой приложения, поэтому у правила
    /// есть внешний источник данных — job пробера. Job обязан существовать в обоих конфигах:
    /// правило одно на оба окружения, и без job оно не сработает никогда.
    /// </summary>
    /// <remarks>
    /// Отказ здесь тихий: правило загрузится, Prometheus не пожалуется, алерт просто не
    /// сможет перейти в firing — при отсутствии серии выражение <c>probe_success == 0</c>
    /// не даёт ни одного результата. То есть удаление job выглядит как «всё хорошо».
    /// </remarks>
    [Fact]
    public void The_readiness_probe_job_backs_the_readiness_alert_in_both_environments()
    {
        foreach (var config in new[] { LocalConfig, StagingConfig })
        {
            var content = string.Join('\n', Meaningful(config));
            Assert.Contains("job_name: testapp-readiness", content, StringComparison.Ordinal);
            Assert.Contains("/health/ready", content, StringComparison.Ordinal);
            Assert.Contains("blackbox-exporter:9115", content, StringComparison.Ordinal);
        }

        var rules = File.ReadAllText(Path.Combine(RepositoryRoot(), "deploy/prometheus/alerts.yml"));
        Assert.Contains("probe_success{job=\"testapp-readiness\"}", rules, StringComparison.Ordinal);
        Assert.Contains("up{job=\"testapp-readiness\"}", rules, StringComparison.Ordinal);

        // Модуль пробы, на который ссылается scrape config, обязан существовать в конфиге
        // экспортера: неизвестный модуль даёт 400 на каждой пробе, то есть постоянный page.
        var blackbox = File.ReadAllText(Path.Combine(RepositoryRoot(), "deploy/blackbox/blackbox.yml"));
        Assert.Contains("testapp_ready:", blackbox, StringComparison.Ordinal);
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
