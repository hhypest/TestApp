# Документация TestApp

> Состояние документации: `beta-ddd`, baseline 2026-08-16. Persistence/runtime/CI контур переведён на PostgreSQL 18; exact-head evidence определяется последними GitHub Actions runs ветки.

Этот каталог является навигационной точкой по архитектуре, бизнес-модели, API, persistence, безопасности, эксплуатации, тестированию и плану развития TestApp.

## Как читать документацию

Документы разделяют четыре типа информации:

- **Implemented** — поведение существует в текущем коде и должно подтверждаться тестами/CI.
- **Verifying** — код или automation существуют, но обязательный exit gate ещё не завершился green.
- **Planned** — согласованный технический следующий шаг, но код ещё не считается реализованным.
- **Decision required** — продуктовая или архитектурная гипотеза; до отдельного решения она не должна восприниматься как обязательство.

Если документация расходится с кодом, приоритет источников истины следующий:

1. Domain/Application/Infrastructure/API code и database migrations.
2. Automated tests, generated OpenAPI и exact-head CI evidence.
3. `CURRENT_STATE.md` — фактический снимок текущего baseline.
4. `ROADMAP.md` — milestones и exit gates.
5. `FEATURE_PLAN.md` — атомарные feature statuses/dependencies.
6. Тематические документы — долговечные contracts, design и runbooks.

## Карта документов

| Документ | Назначение |
|---|---|
| [CURRENT_STATE.md](CURRENT_STATE.md) | Точный снимок реализованных возможностей, ограничений и технического долга |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Модульная архитектура, зависимости проектов, CQRS flow, request pipeline и background workers |
| [DOMAIN_MODEL.md](DOMAIN_MODEL.md) | Aggregates, value objects, lifecycle, инварианты, scoring и state transitions |
| [API.md](API.md) | Canonical `/api/v1`, legacy compatibility, endpoints, authorization, DTO, ошибки, pagination и idempotency |
| [PERSISTENCE.md](PERSISTENCE.md) | PostgreSQL 18, EF mappings, schema, migrations, optimistic concurrency и advisory locks |
| [EVENTS_AND_OUTBOX.md](EVENTS_AND_OUTBOX.md) | Domain/integration events, transactional Outbox, RabbitMQ, retry/dead-letter и delivery semantics |
| [SECURITY.md](SECURITY.md) | Keycloak/JWT claims, роли/policies, owner isolation, rate limiting, audit и remaining security backlog |
| [OPERATIONS.md](OPERATIONS.md) | Docker Compose, migrations, health, OpenTelemetry, RabbitMQ, runbooks и конфигурация |
| [BACKUP_RESTORE.md](BACKUP_RESTORE.md) | Logical backup/restore baseline, RPO/RTO и recovery drill |
| [SLO_ALERTS.md](SLO_ALERTS.md) | Operational metrics, SLO thresholds, alert policy и triage |
| [PERFORMANCE.md](PERFORMANCE.md) | D6 load/capacity scenarios, thresholds и verification status |
| [TESTING.md](TESTING.md) | Стратегия тестирования, текущие suites, CI gates и требования к новым фичам |
| [CODE_REVIEW.md](CODE_REVIEW.md) | Правила структуры файлов, размера изменений и reviewability checklist |
| [DECISIONS.md](DECISIONS.md) | Зафиксированные архитектурные решения и сознательно неиспользуемые технологии |
| [ROADMAP.md](ROADMAP.md) | Последовательная дорожная карта релизов и технических этапов |
| [FEATURE_PLAN.md](FEATURE_PLAN.md) | Детальный каталог фич с ID, приоритетами, зависимостями и критериями готовности |
| [CHANGELOG.md](../CHANGELOG.md) | Пользовательский changelog по версиям/фазам roadmap (Keep a Changelog + SemVer) |

## Технологический baseline

**Implemented:**

- .NET 10;
- DDD + Clean Architecture + CQRS в modular monolith;
- EF Core 10.0.11 + Npgsql EF provider 10.0.3;
- PostgreSQL 18;
- Keycloak как внешний Identity Provider;
- Minimal API + JWT Bearer;
- RabbitMQ 4.3.x transport для Outbox;
- OpenTelemetry 1.17.0;
- Docker/Compose;
- GitHub Actions с реальными PostgreSQL/RabbitMQ service containers.

## Базовый словарь

- **Test** — изменяемое рабочее определение теста.
- **PublishedTestRevision** — immutable snapshot опубликованного теста.
- **Assignment** — назначение конкретной revision пользователю или группе.
- **Attempt** — попытка конкретного пользователя по конкретному assignment/revision.
- **Actor** — пользователь, идентифицированный внешним `sub` из Keycloak.
- **Domain event** — внутреннее бизнес-событие aggregate.
- **Integration event** — явно отмеченное событие, разрешённое к выходу через Outbox.
- **Outbox** — durable запись integration event в той же PostgreSQL transaction, что и бизнес-изменение.
- **Idempotency lease** — PostgreSQL advisory lock, сериализующий повторяемые unsafe operations между API instances.

## Правило актуализации

Любая фича, меняющая публичный API, domain invariant, database schema, security model, runtime dependency или delivery semantics, должна обновлять соответствующий документ в `docs/` в том же PR/commit series. Новая planned-фича считается завершённой только после переноса из roadmap в раздел Implemented и появления regression tests.
