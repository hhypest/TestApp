using System.Net;
using System.Text.Json;
using Xunit;

namespace TestApp.IntegrationTests;

/// <summary>
/// Окончательная заморозка контракта v1 (issue #22, гейт `docs/ROADMAP.md` §7 пункт 1).
/// </summary>
/// <remarks>
/// <para>
/// <c>OpenApiSnapshotTests</c> ловит <em>любое</em> изменение документа и требует обновить снимок.
/// Этого достаточно, пока заморозка черновая: изменение видно в diff и обсуждается на ревью. После
/// объявления заморозки этого мало — снимок обновляется одной командой, и различить в его diff
/// «добавили поле» и «удалили поле» может только внимательный читатель, а ломает клиентов
/// исключительно второе.
/// </para>
/// <para>
/// Поэтому здесь проверяется не совпадение, а <em>включение</em>: всё, что было в контракте на
/// момент заморозки, обязано в нём остаться. Дополнения проходят молча — политика
/// <c>docs/API.md</c> §1 разрешает их внутри v1. Удаление или переименование эндпоинта, кода
/// ответа, поля тела или свойства DTO падает здесь, а не выясняется у потребителя.
/// </para>
/// <para>
/// У <c>docs/openapi/v1-frozen-surface.json</c> намеренно нет автоматической регенерации: команда
/// «обнови и закоммить» превратила бы заморозку в описание того, что получилось. Файл правится
/// руками и только вместе с решением о v2 — тогда diff показывает ровно то, от чего отказались.
/// </para>
/// <para>
/// Поля ответов перечислены полными путями (<c>items[].id.value</c>), потому что тела ответов
/// в документе встроены, а не вынесены в <c>components</c>: ограничиться списком схем значило бы
/// заморозить формы запросов и не заморозить формы ответов.
/// </para>
/// </remarks>
public sealed class OpenApiFrozenSurfaceTests
{
    private const string SurfaceRelativePath = "docs/openapi/v1-frozen-surface.json";

    [Fact]
    public async Task Nothing_frozen_into_v1_has_disappeared_from_the_published_contract()
    {
        var ct = TestContext.Current.CancellationToken;
        using var live = JsonDocument.Parse(await FetchDocumentAsync(ct));
        using var frozen = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), SurfaceRelativePath), ct));

        var liveOperations = ReadOperations(live.RootElement);
        var liveSchemas = ReadSchemas(live.RootElement);
        var violations = new List<string>();

        foreach (var frozenOperation in frozen.RootElement.GetProperty("operations").EnumerateObject())
        {
            if (!liveOperations.TryGetValue(frozenOperation.Name, out var actual))
            {
                violations.Add($"операция исчезла: {frozenOperation.Name}");
                continue;
            }

            var expectedId = frozenOperation.Value.GetProperty("operationId").GetString();
            if (actual.OperationId != expectedId)
                violations.Add($"{frozenOperation.Name}: operationId '{expectedId}' -> '{actual.OperationId}'");

            foreach (var code in Strings(frozenOperation.Value, "responses").Where(code => !actual.Responses.Contains(code)))
                violations.Add($"{frozenOperation.Name}: исчез код ответа {code}");

            foreach (var field in Strings(frozenOperation.Value, "responseFields").Where(field => !actual.Fields.Contains(field)))
                violations.Add($"{frozenOperation.Name}: исчезло поле ответа {field}");
        }

        foreach (var frozenSchema in frozen.RootElement.GetProperty("schemas").EnumerateObject())
        {
            if (!liveSchemas.TryGetValue(frozenSchema.Name, out var actual))
            {
                violations.Add($"схема исчезла: {frozenSchema.Name}");
                continue;
            }

            foreach (var property in Strings(frozenSchema.Value, "properties").Where(property => !actual.Properties.Contains(property)))
                violations.Add($"{frozenSchema.Name}: исчезло свойство {property}");

            // Ужесточение уже существующего поля запроса ломает клиента, написанного до него.
            // Правило намеренно ограничено полями, которые были в контракте на момент заморозки:
            // новое поле §1 разрешает добавлять, и генератор всё равно объявит его обязательным —
            // без этого ограничения проверка запрещала бы то, что политика прямо разрешает.
            //
            // Сегодня правило спит: генератор перечисляет в required все поля record'а, включая
            // nullable, поэтому необязательных полей в запросах нет ни одного (см. ADR-035).
            // Оно оживёт ровно тогда, когда появится первое действительно необязательное поле.
            if (frozenSchema.Value.TryGetProperty("request", out _))
            {
                var wasRequired = Strings(frozenSchema.Value, "required").ToHashSet(StringComparer.Ordinal);
                var wasDeclared = Strings(frozenSchema.Value, "properties").ToHashSet(StringComparer.Ordinal);
                foreach (var property in actual.Required
                             .Where(property => wasDeclared.Contains(property) && !wasRequired.Contains(property)))
                    violations.Add($"{frozenSchema.Name}: поле {property} стало обязательным в запросе");
            }
        }

        Assert.True(violations.Count == 0,
            "Контракт v1 заморожен, но потерял часть поверхности — это ломающее изменение и требует v2 " +
            "(политика docs/API.md §1):" + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(x => "  - " + x)) + Environment.NewLine +
            $"Если отказ осознан, {SurfaceRelativePath} правится руками в том же change set.");
    }

    private sealed record LiveOperation(string? OperationId, HashSet<string> Responses, HashSet<string> Fields);

    private sealed record LiveSchema(HashSet<string> Properties, HashSet<string> Required);

    private static Dictionary<string, LiveOperation> ReadOperations(JsonElement root)
    {
        var result = new Dictionary<string, LiveOperation>(StringComparer.Ordinal);
        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                if (method.Value.ValueKind != JsonValueKind.Object ||
                    !method.Value.TryGetProperty("operationId", out var operationId))
                    continue;

                var responses = new HashSet<string>(StringComparer.Ordinal);
                var fields = new HashSet<string>(StringComparer.Ordinal);
                if (method.Value.TryGetProperty("responses", out var responseObject))
                {
                    foreach (var response in responseObject.EnumerateObject())
                        responses.Add(response.Name);

                    if (responseObject.TryGetProperty("200", out var success) &&
                        success.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("application/json", out var json) &&
                        json.TryGetProperty("schema", out var schema))
                        CollectFields(schema, string.Empty, fields);
                }

                result[$"{method.Name.ToUpperInvariant()} {path.Name}"] =
                    new LiveOperation(operationId.GetString(), responses, fields);
            }
        }

        return result;
    }

    private static void CollectFields(JsonElement schema, string prefix, HashSet<string> into)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return;

        if (schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()?.Split('/')[^1];
            into.Add($"{prefix}->{name}");
            return;
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                var child = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
                into.Add(child);
                CollectFields(property.Value, child, into);
            }
        }

        if (schema.TryGetProperty("items", out var items))
            CollectFields(items, prefix + "[]", into);
    }

    private static Dictionary<string, LiveSchema> ReadSchemas(JsonElement root)
    {
        var result = new Dictionary<string, LiveSchema>(StringComparer.Ordinal);
        if (!root.TryGetProperty("components", out var components) ||
            !components.TryGetProperty("schemas", out var schemas))
            return result;

        foreach (var schema in schemas.EnumerateObject())
        {
            var properties = new HashSet<string>(StringComparer.Ordinal);
            if (schema.Value.TryGetProperty("properties", out var propertyObject))
                foreach (var property in propertyObject.EnumerateObject())
                    properties.Add(property.Name);

            var required = new HashSet<string>(StringComparer.Ordinal);
            if (schema.Value.TryGetProperty("required", out var requiredArray))
                foreach (var item in requiredArray.EnumerateArray())
                    required.Add(item.GetString()!);

            result[schema.Name] = new LiveSchema(properties, required);
        }

        return result;
    }

    private static IEnumerable<string> Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var array)
            ? array.EnumerateArray().Select(x => x.GetString()!)
            : [];

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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TestApp.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new Xunit.Sdk.XunitException("Не найден корень репозитория (TestApp.slnx).");
    }
}
