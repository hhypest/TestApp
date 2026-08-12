# Документация TestApp

> Состояние документации: `beta-ddd`, baseline 2026-08-12, исходный код проверен на commit `024b932303152438fb1e611381b449735ac46fad` перед созданием этого комплекта документов.

Этот каталог является навигационной точкой по архитектуре, бизнес-модели, API, persistence, безопасности, эксплуатации, тестированию и плану развития TestApp.

## Как читать документацию

Документы разделяют три типа информации:

- **Implemented** — поведение существует в текущем коде и должно подтверждаться тестами/CI.
- **Planned** — согласованный технический следующий шаг, но код ещё не считается реализованным.
- **Decision required** — продуктовая или архитектурная гипотеза; до отдельного решения она не должна восприниматься как обязательство.

Если документация расходится с кодом, приоритет источников истины следующий:

1. Domain/Application code и database migrations.
2. API routing/contracts в `src/TestApp.Api/Program.cs` и OpenAPI document.
3. Integration/domain/application tests.
4. Документы в `docs/`.
5. Roadmap/feature backlog — это план, а не описание текущего поведения.

## Карта документов

| Документ | Назначение |
|---|---|
| [CURRENT_STATE.md](CURRENT_STATE.md) | Точный снимок реализованных возможностей, ограничений и технического долга |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Модульная архитектура, зависимости проектов, CQRS flow, request pipeline и background workers |
| [DOMAIN_MODEL.md](DOMAIN_MODEL.md) | Aggregates, value objects, lifecycle, инварианты, scoring и state transitions |
| [API.md](API.md) | Canonical `/api/v1`, legacy compatibility, endpoints, authorization, DTO, ошибки, pagination и idempotency |
| [PERSISTENCE.md](PERSISTENCE.md) | MariaDB 12.3, EF mappings, schema, migrations, optimistic concurrency и advisory locks |
| [EVENTS_AND_OUTBOX.md](EVENTS_AND_OUTBOX.md) | Domain/integration events, transactional Outbox, RabbitMQ, retry/dead-letter и delivery semantics |
| [SECURITY.md](SECURITY.md) | Keycloak/JWT claims, роли/policies, ownership gaps, rate limiting, audit и production security backlog |
| [OPERATIONS.md](OPERATIONS.md) | Docker Compose, migrations, health, OpenTelemetry, RabbitMQ, runbooks и конфигурация |
| [TESTING.md](TESTING.md) | Стратегия тестирования, текущие suites, CI gates и требования к новым фичам |
| [DECISIONS.md](DECISIONS.md) | Зафиксированные архитектурные решения и сознательно неиспользуемые технологии |
| [ROADMAP.md](ROADMAP.md) | Последовательная дорожная карта релизов и технических этапов |
| [FEATURE_PLAN.md](FEATURE_PLAN.md) | Детальный каталог фич с ID, приоритетами, зависимостями и критериями готовности |

## Технологический baseline

**Implemented:**

- .NET 10;
- DDD + Clean Architecture + CQRS в modular monolith;
- EF Core 9.0.18 + Pomelo 9.0.0;
- MariaDB 12.3;
- Keycloak как внешний Identity Provider;
- Minimal API + JWT Bearer;
- RabbitMQ 4.3.x transport для Outbox;
- OpenTelemetry 1.17.0;
- Docker/Compose;
- GitHub Actions с реальными MariaDB/RabbitMQ service containers.

## Базовый словарь

- **Test** — изменяемое рабочее определение теста.
- **PublishedTestRevision** — immutable snapshot опубликованного теста.
- **Assignment** — назначение конкретной revision пользователю или группе.
- **Attempt** — попытка конкретного пользователя по конкретному assignment/revision.
- **Actor** — пользователь, идентифицированный внешним `sub` из Keycloak.
- **Domain event** — внутреннее бизнес-событие aggregate.
- **Integration event** — явно отмеченное событие, разрешённое к выходу через Outbox.
- **Outbox** — durable запись integration event в той же MariaDB transaction, что и бизнес-изменение.
- **Idempotency lease** — MariaDB advisory lock, сериализующий повторяемые unsafe operations между API instances.

## Правило актуализации

Любая фича, меняющая публичный API, domain invariant, database schema, security model, runtime dependency или delivery semantics, должна обновлять соответствующий документ в `docs/` в том же PR/commit series. Новая planned-фича считается завершённой только после переноса из roadmap в раздел Implemented и появления regression tests.