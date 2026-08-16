# Стратегия тестирования TestApp

> Статус: **Implemented test baseline** + требования к расширению coverage.

## 1. Цель

Тесты должны защищать не количество строк кода, а архитектурные и бизнес-гарантии:

- aggregate invariants;
- immutable revision history;
- authorization/data isolation;
- concurrency/idempotency;
- PostgreSQL-specific behavior;
- migrations;
- Outbox/RabbitMQ delivery;
- HTTP contract;
- production container migration path.

## 2. Test suites

Solution содержит три .NET test projects:

```text
tests/TestApp.Domain.Tests
tests/TestApp.Application.Tests
tests/TestApp.IntegrationTests
```

Репозиторий также содержит отдельный импортируемый API test project:

```text
tests/TestApp.Postman
```

Он включает Postman Collection v2.1, local environment, dependency-free validator и Newman CI. Collection использует real Keycloak fixture users и выполняет сквозной author -> admin -> student -> reviewer/operations flow по canonical `/api/v1`.

### Postman/Newman coverage

- health и OpenAPI;
- получение JWT для `author`, `admin`, `student`;
- все реализованные canonical API routes;
- ETag/If-Match и idempotency replay/fingerprint;
- authorization/validation/precondition negative paths;
- single/bulk assignments, attempt submit/timeout и reviewer projections;
- audit/Outbox reads;
- dead-letter detail/requeue/discard как manual opt-in operations.

Запуск и data-cleanup описаны в [`tests/TestApp.Postman/README.md`](../tests/TestApp.Postman/README.md).

## 3. Domain tests

Current file:

```text
TestAggregateTests.cs
```

Проверяет core business behavior без EF/HTTP:

- Test lifecycle;
- publication validation;
- settings;
- immutable published revision;
- scoring/outcome;
- attempt deadline;
- aggregate invariants.

### Правило

Если новая business rule может быть проверена без Infrastructure, основной regression test должен находиться в Domain.Tests.

## 4. Application tests

Current file:

```text
StartAttemptTests.cs
```

Фокус:

- orchestration use case;
- target user/group validation;
- availability;
- attempt limit behavior через abstractions;
- request validation;
- handler-level business mapping;
- actor-scoped idempotent replay после cancellation, expiry и потери group membership;
- повторная eligibility validation для нового key и изоляция key между actors.

### Текущий gap

Application unit coverage заметно меньше integration coverage. Новые complex handlers должны получать targeted unit tests, если сценарии можно проверить быстрее и точнее без PostgreSQL.

## 5. Integration tests

Integration tests являются критической частью проекта и работают против real PostgreSQL service в CI. RabbitMQ-specific tests работают против real RabbitMQ service.

Текущие test areas/files включают:

### `ApiBoundaryContractTests.cs`

Проверяет:

- canonical `/api/v1`;
- legacy `/api/*` compatibility;
- anonymous OpenAPI;
- correlation header;
- audit boundary/contracts.

### `AuditStatusContractTests.cs`

Проверяет равенство финального HTTP response и persisted audit status для handled binding `400`, concurrency `409`, precondition `412` и unhandled `500`.

### `ApiHostTests.cs`

Полный HTTP vertical flow через `WebApplicationFactory`:

```text
author create
  -> add question/options
  -> publish
admin assign
student start
  -> answer
  -> submit
  -> retry submit
  -> own result
author reviewer list/detail
admin operations
```

Также:

- role authorization;
- bulk assignment idempotency;
- admin assignment queries/statistics;
- forbidden paths.

### `ArchitectureAndIdentityTests.cs`

Проверяет assembly dependency direction и Keycloak claim mapping.

### API contract suites

- `ApiLifecycleContractTests.cs` — legacy enable/deprecation/sunset/retirement;
- `ConcurrencyHttpContractTests.cs` — ETag/If-Match/428/412/409;
- `IdempotencyHttpContractTests.cs` — header/body compatibility, mismatch и fingerprint reuse;
- `EmptyBodyIdempotencyTests.cs` — настоящий zero-length body (без `Content-Type`/payload) с header-only `Idempotency-Key` для publish/start attempt/submit (API-009);
- `RequestValidationContractTests.cs` — malformed binding/DataAnnotations/enum normalization;
- `OpenApiContractTests.cs` — serialized enriched document;
- `EdgeSecurityTests.cs` — forwarded headers, CORS, transport headers, rate limits и OpenAPI exposure;
- `TestOwnershipTests.cs` — cross-author write/read/reviewer isolation.

### Operational suites

- `RuntimeConfigurationTests.cs` — fail-fast typed configuration;
- `OperationalRetentionTests.cs` — bounded cleanup и protected Outbox states;
- `OutboxDeadLetterManagementTests.cs` / `OutboxDeadLetterApiTests.cs` — locked audited requeue/discard и payload-safe HTTP contract;
- `OperationalMetricsTests.cs` — custom low-cardinality metric contract.

### `PostgreSqlVersionTests.cs`

Выполняет `SHOW server_version_num` и требует actual PostgreSQL runtime >= 18 (`>= 180000`).

Назначение: Docker tag сам по себе не считается достаточным доказательством runtime baseline.

### `PersistenceBehaviorTests.cs`

Проверяет:

- migrations на пустой database;
- assignment persistence;
- optimistic concurrency conflict;
- start request idempotency;
- PostgreSQL advisory idempotency lease между разными DbContexts.

### `PublishedRevisionPersistenceTests.cs`

Regression test immutable revision JSON round-trip.

Это защищает от EF converter/backing-field materialization regressions.

### `OverdueAttemptExpirationTests.cs`

Проверяет automatic expiration:

```text
expired InProgress -> TimedOut
```

с persisted score/outcome/concurrency state.

### `OutboxDeliveryTests.cs`

Проверяет retry/dead-letter/success semantics Outbox processor.

### `WorkerCycleResilienceTests.cs`

Проверяет cycle-level resilience `OutboxProcessor`/`OverdueAttemptProcessor` (STAB-003): симулирует однократный сбой `IServiceScopeFactory.CreateScope()` на первом poll cycle и проверяет, что worker логирует ошибку, продолжает работу на следующем `PeriodicTimer` tick и не fault-ит хостовой `BackgroundService.ExecuteTask`.

### `RabbitMqOutboxPublisherTests.cs`

Проверяет реальный broker publish contract:

- exchange routing;
- payload;
- `MessageId/EventId`;
- type/content-type;
- persistence semantics.

### `RabbitMqOutboxPipelineTests.cs`

Проверяет полный flow:

```text
outbox row
 -> OutboxProcessor
 -> RabbitMQ
 -> broker delivery
 -> ProcessedAt
```

### `RabbitMqHealthCheckTests.cs`

Проверяет active RabbitMQ readiness connection/channel/exchange access.

## 6. PostgreSQL test database strategy

`PostgreSqlTestDatabase`:

- подключается admin connection из `TESTAPP_POSTGRES_ADMIN` к CI/local PostgreSQL;
- создаёт уникальную temporary database на test;
- использует `UseNpgsql` с pooling disabled для disposable database;
- после test завершает оставшиеся target sessions и удаляет database.

Преимущества:

- tests изолированы;
- migrations реально исполняются;
- constraints/indexes/provider behavior не подменяются SQLite/in-memory provider;
- можно безопасно запускать integration tests параллельно при отсутствии конфликтующих external resources.

## 7. Почему SQLite/in-memory provider запрещён для persistence regressions

Provider-specific behavior, которое должно проверяться на PostgreSQL:

- SQL dialect;
- DDL/migrations;
- `uuid`/`timestamptz`/decimal mappings;
- composite indexes;
- unique constraints;
- PostgreSQL advisory lease не допускает превышения attempt limit при concurrent starts;
- `pg_try_advisory_lock/pg_advisory_unlock`;
- concurrency update semantics;
- locale/collation behavior;
- `jsonb` immutable snapshot round-trip.

Поэтому persistence test на SQLite не является эквивалентом production validation.

## 8. RabbitMQ test strategy

CI environment variable:

```text
TESTAPP_RABBITMQ
```

RabbitMQ tests используют real broker service container.

Для broker tests важно:

- уникальные exchange/queue names на test;
- cleanup topology;
- не зависеть от management API, если AMQP достаточно;
- не оставлять race между queue binding и publish;
- проверять publisher lifecycle/disposal.

## 9. API authentication in tests

HTTP integration tests заменяют production JWT scheme на test authentication scheme на уровне `ConfigureTestServices`.

Общий bootstrap находится в `ApiTestHost.cs`: он задаёт test database/Keycloak settings, регистрирует единый authentication handler и формирует request headers. Конкретный contract suite добавляет только scenario-specific host/service overrides; например lifecycle flags, edge-security settings или failing `IUnitOfWork`.

Test principal формирует:

- `sub` через `X-Test-User`;
- roles через `X-Test-Roles`.

Это позволяет тестировать существующий authorization/application actor flow, не поднимая Keycloak для каждого HTTP test.

### Отдельно

Keycloak claim mapping должен иметь отдельные integration/unit tests. Полный real-Keycloak token E2E можно добавить как slower environment test, но он не должен заменять быстрый API suite.

## 10. CI pipeline

GitHub Actions `dotnet` workflow:

1. PostgreSQL 18 service container;
2. RabbitMQ 4.3.1 management service;
3. checkout;
4. setup .NET 10;
5. `dotnet restore TestApp.slnx` + HIGH/CRITICAL NuGet audit;
6. Release build;
7. restore pinned `dotnet-ef` and verify `migrations has-pending-model-changes`;
8. Release tests;
9. `docker compose config --quiet`;
10. build production API image;
11. run production image `--migrate` against PostgreSQL;
12. create logical backup and verify isolated restore/business marker;
13. cleanup containers.

Отдельный `security` workflow выполняет repository secret scan, production-image HIGH/CRITICAL scan и CycloneDX SBOM artifact.

Отдельный `performance` workflow поднимает production-shaped API/PostgreSQL/RabbitMQ/Keycloak stack, получает real JWT и проверяет k6 HTTP scenarios, expiration storm и Outbox recovery.

Отдельный `postman` workflow валидирует importable artifacts, поднимает тот же local stack и запускает полный Postman API contract через Newman. JUnit report сохраняется как Actions artifact.

### Merge/release gate

Нельзя считать commit production-capable, если зелёны unit tests, но не прошёл migration-only container step.

## 11. Test requirements по типу изменения

### Domain rule

Обязательно:

- Domain test happy path;
- boundary values;
- invalid transition;
- regression existing state.

### New command

Обязательно:

- Application/domain test;
- HTTP happy path;
- authorization negative path;
- idempotency/concurrency test если операция unsafe/retryable.

### New query

Обязательно:

- SQL-side filtering/paging integration test;
- visibility/authorization;
- student sensitive-data check если DTO связан с assessment content.

### Database migration

Обязательно:

- empty DB migrate;
- existing-data upgrade scenario, если изменение nontrivial;
- schema/index regression;
- production-image `--migrate`.

### Integration event

Обязательно:

- serialization contract;
- EventId/MessageId;
- routing key;
- no-sensitive-data assertion;
- duplicate delivery consumer expectation/contract.

### Security change

Обязательно:

- anonymous;
- wrong role;
- wrong resource owner/target;
- correct role/owner;
- no data leakage.

## 12. Test data principles

- IDs генерировать typed factory/Guid v7 там, где это соответствует production.
- Fixed clock использовать для deadline/time rules.
- Не использовать `Thread.Sleep` для бизнес-тайминга, если можно injected clock.
- Для external broker synchronization использовать deterministic topology/confirm semantics.
- Test должен самостоятельно создавать required state, а не зависеть от порядка выполнения других tests.

## 13. Current coverage gaps

До 1.0 необходимо расширить:

### P1

- property-style tests для scoring/question invariants;
- application handler suite beyond StartAttempt;
- concurrency test submit vs background timeout;
- multiple API replicas idempotency scenario;
- deterministic pagination при одинаковых timestamps;
- high-cardinality reviewer/admin query regression + query-plan evidence;
- strong-ID/AttemptScore/error mapping invariants;
- real Keycloak smoke integration.

### P2

- endurance/soak Outbox;
- failover/restart PostgreSQL/RabbitMQ scenarios;
- staging/platform backup restore drill;
- contract tests for frontend/SDK.

## 14. Performance testing baseline

Реализованный D6 workflow покрывает:

- bulk assignments;
- simultaneous attempt starts;
- high-frequency answer writes;
- mass deadline expiration;
- reviewer result pagination;
- Outbox backlog recovery.

Thresholds и artifacts описаны в `PERFORMANCE.md`.

Verification status: **PASSED** на PostgreSQL implementation commit `9916b98`. Full run подтвердил 8771/8771 checks, HTTP failure rate 0, expiration drain 1247 -> 0 за 14 s и Outbox drain 100 -> 0 за 1 s через RabbitMQ.

Для staging/soak дополнительно измерять:

- p50/p95/p99 latency;
- requests/sec;
- DB CPU/connections/locks;
- rows scanned;
- deadlocks/concurrency conflicts;
- Outbox lag;
- worker batch duration.

## 15. Test naming

Предпочтительный формат поведения:

```text
Operation_condition_expectedResult
```

Например:

```text
Same_start_request_returns_same_attempt_id
Concurrent_aggregate_update_is_rejected
Processor_marks_message_processed_only_after_RabbitMQ_delivery
```

Название должно описывать гарантию, а не implementation detail.

## 16. Definition of Done для тестов

Фича не Done, если:

- нет regression test ключевого business rule;
- schema change не проверен PostgreSQL;
- новый endpoint не имеет auth negative test;
- sensitive DTO не проверен на leakage;
- unsafe command не имеет retry/concurrency story;
- integration event не имеет contract test;
- CI production image/migration path не зелёный.
