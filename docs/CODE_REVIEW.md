# Reviewability и правила изменения кода

Этот документ фиксирует минимальные правила, благодаря которым изменения TestApp остаются пригодными для содержательного code review. Цель — не дробить код до микрофайлов, а сделать границы ответственности видимыми из структуры diff.

## 1. Организация production-кода

- `Program.cs` является composition root: загружает конфигурацию, регистрирует зависимости, собирает middleware pipeline и подключает endpoint-модули. Бизнес-логика и подробное описание маршрутов в нём не размещаются.
- HTTP request contracts находятся в `TestApp.Api/ApiRequests.cs`; операционные endpoint-specific contracts могут оставаться рядом со своим endpoint-модулем.
- Маршруты группируются по пользовательскому сценарию в `TestApp.Api/Endpoints/`. Endpoint преобразует HTTP input в command/query и отображает результат в HTTP response, но не реализует domain invariant.
- Runtime options/loaders группируются по concern в `TestApp.Api/Configuration/`; OpenAPI transformation и статический operation catalog находятся в `TestApp.Api/OpenApi/`.
- Application command/query и соответствующий handler располагаются рядом и группируются по одному use-case family. Файл не должен превращаться в каталог несвязанных типов.
- Domain aggregate может быть крупнее обычного service-файла, если размер обусловлен единым consistency boundary. HTTP, EF Core и broker concerns в Domain запрещены независимо от размера файла.
- Infrastructure группируется по адаптеру или persistence-сценарию. SQL/EF projection не переносится в Application ради уменьшения числа строк.

## 2. Размер и связность

Числа ниже — сигнал для обсуждения, а не автоматический CI gate:

- production-файл больше примерно 250 строк;
- больше 8 top-level типов в одном файле;
- endpoint-модуль одновременно обслуживает несколько несвязанных route groups;
- composition root содержит реализацию use case, mapping ответа или вспомогательный алгоритм.

Превышение допустимо для cohesive aggregate, generated migration/snapshot, protocol contract или таблицы статической конфигурации. В таком случае связность важнее механического деления.

## 3. Форма изменений

- Behavior-preserving рефакторинг и изменение продукта по возможности выполняются раздельными commits. Это позволяет сравнить публичные контракты до и после структурного изменения.
- Один commit должен иметь одну проверяемую цель. Массовое переименование, форматирование всего solution и функциональная правка не смешиваются.
- Публичный API, JSON shape, status codes, authorization, rate limits, idempotency, ETag/concurrency и Outbox semantics считаются контрактами и требуют regression coverage.
- Новая абстракция добавляется только при наличии реальной границы или как минимум двух потребителей. Уменьшение числа строк само по себе не является основанием для interface/helper.
- Generated migrations и snapshots не форматируются вручную и не используются как ориентир размера production-файлов.

## 4. Удаление устаревшего кода

Код удаляется как obsolete/dead только после проверки всех применимых путей использования:

- отсутствуют compile-time consumers;
- тип не обнаруживается через DI scanning, EF configuration, serialization, reflection или tooling;
- код не обслуживает действующий HTTP/configuration/persistence/event contract;
- compatibility window, migration/cutover и rollback retention явно завершены, если они применимы;
- удаление подтверждается релевантными regression tests и exact-head CI.

Неиспользуемые abstractions и package references «на будущее» удаляются. Повторно они добавляются вместе с первым реальным потребителем. Legacy `/api/*`, body idempotency compatibility и MariaDB cutover/rollback документация не считаются dead code, пока их lifecycle contracts остаются действующими.

## 5. Минимальный checklist автора

Перед публикацией изменения автор проверяет:

1. Из diff понятно, какая ответственность изменяется и почему.
2. Границы Domain/Application/Infrastructure/API не нарушены.
3. В feature change отсутствует несвязанный formatting noise.
4. Контракты HTTP/persistence/events обновлены вместе с тестами и документацией.
5. Пройдены релевантные unit/integration tests и exact-head GitHub Actions gates.

Ревьюер сначала проверяет контракты и инварианты, затем failure paths и только после этого локальный стиль. Форматирование не должно скрывать смысловую часть diff.
