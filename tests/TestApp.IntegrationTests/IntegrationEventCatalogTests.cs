using System.Reflection;
using TestApp.Domain.Events;
using Xunit;

namespace TestApp.IntegrationTests;

/// <summary>
/// Каталог интеграционных событий (гейт `docs/ROADMAP.md` §7 пункт 9, ADR-033).
/// </summary>
/// <remarks>
/// <para>
/// На 1.0 каталог намеренно пуст: транспорт Outbox/RabbitMQ полностью реализован и протестирован,
/// но ни один production-тип не реализует <see cref="IIntegrationEvent"/>, поэтому наружу не уходит
/// ничего. Это не пробел, а решение — публиковать внутренние доменные события как внешний контракт
/// хуже, чем не публиковать ничего (ADR-012), а проектировать контракт без единого потребителя
/// значит замораживать догадку.
/// </para>
/// <para>
/// Тест удерживает именно это состояние. Он падает в тот момент, когда появляется первый
/// production-тип, реализующий <see cref="IIntegrationEvent"/> — и это правильно: такое событие
/// мгновенно становится публичным контрактом, который обязан попасть в каталог
/// `docs/EVENTS_AND_OUTBOX.md` §13 и получить версионирование по §14 до того, как уедет к потребителю.
/// Молча добавить событие нельзя.
/// </para>
/// <para>
/// Тестовые двойники (<c>TestIntegrationEvent</c>, <c>PipelineIntegrationEvent</c>) живут в тестовых
/// сборках и каталогом не являются — сканируются только production-сборки.
/// </para>
/// </remarks>
public sealed class IntegrationEventCatalogTests
{
    /// <summary>
    /// Каталог, объявленный в `docs/EVENTS_AND_OUTBOX.md` §13. Пополнять этот набор нужно
    /// одновременно с документом, а не вместо него.
    /// </summary>
    private static readonly string[] PublishedCatalog = [];

    private static Assembly[] ProductionAssemblies =>
    [
        typeof(TestApp.Domain.Tests.Test).Assembly,
        typeof(TestApp.Application.Abstractions.IUnitOfWork).Assembly,
        typeof(TestApp.Infrastructure.Persistence.AppDbContext).Assembly,
        typeof(Program).Assembly
    ];

    [Fact]
    public void The_published_catalog_matches_the_documented_one()
    {
        var actual = ProductionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IIntegrationEvent).IsAssignableFrom(type))
            .Where(type => type is { IsInterface: false, IsAbstract: false })
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            PublishedCatalog.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            actual);
    }

    /// <summary>
    /// Смысл пустого каталога держится на одном факте: `IIntegrationEvent` — отдельный маркер, а не
    /// синоним `IDomainEvent`. Если бы Outbox захватывал любое доменное событие, «каталог пуст»
    /// означало бы «наружу уходит всё внутреннее», а не «наружу не уходит ничего».
    /// </summary>
    [Fact]
    public void Domain_events_are_not_integration_events_by_default()
    {
        var domainEvents = typeof(TestApp.Domain.Tests.Test).Assembly
            .GetTypes()
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type))
            .Where(type => type is { IsInterface: false, IsAbstract: false })
            .ToArray();

        Assert.NotEmpty(domainEvents);
        Assert.DoesNotContain(domainEvents, type => typeof(IIntegrationEvent).IsAssignableFrom(type));
    }
}
