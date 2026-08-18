using System.Text.Json;
using Xunit;

namespace TestApp.IntegrationTests;

/// <summary>
/// Каждый успешный ответ обязан описывать своё тело (issue #22).
/// </summary>
/// <remarks>
/// <para>
/// До этой проверки контракт v1 описывал коды ответов, заголовки, параметры и `ProblemDetails`
/// для ошибок — но **ни один** из успешных ответов не описывал тело: все `200` были без `content`.
/// То есть по спецификации нельзя было узнать форму ни одного ответа, а генератор клиента получал
/// набор операций, возвращающих «неизвестно что». Замораживать такой контракт значит замораживать
/// его половину.
/// </para>
/// <para>
/// Причина была не в документации, а в том, что эндпоинты возвращают нетипизированный
/// <c>IResult</c> (<c>Results.Ok</c>, <c>ApiResultMapper.ToHttp</c>), поэтому вывести тип ответа
/// из делегата нечем. Формы объявлены в <c>ApiOperationContracts</c> рядом с operationId и
/// описанием — там же, где уже живёт остальная часть контракта операции.
/// </para>
/// <para>
/// Этот тест удерживает результат: новый эндпоинт без объявленного типа ответа роняет сборку
/// тестов, а не тихо добавляет в спецификацию операцию без схемы.
/// </para>
/// </remarks>
public sealed class OpenApiResponseSchemaTests
{
    [Fact]
    public void Every_successful_response_describes_its_body()
    {
        var document = LoadFrozenContract();
        var missing = new List<string>();

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("responses", out var responses))
                    continue;
                if (!responses.TryGetProperty("200", out var ok))
                    continue;

                if (!ok.TryGetProperty("content", out var content) ||
                    !content.TryGetProperty("application/json", out var json) ||
                    !json.TryGetProperty("schema", out _))
                {
                    missing.Add($"{operation.Name.ToUpperInvariant()} {path.Name}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "Операции с ответом 200 без схемы тела:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing) + Environment.NewLine +
            "Объявите тип ответа в ApiOperationContracts рядом с operationId.");
    }

    /// <summary>
    /// Схема, состоящая из одной ссылки на несуществующий компонент, формально «есть», но
    /// бесполезна. Проверяется, что каждая ссылка разрешается.
    /// </summary>
    [Fact]
    public void Every_response_schema_reference_resolves()
    {
        var document = LoadFrozenContract();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var declared = schemas.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

        var dangling = References(document.RootElement)
            .Where(reference => reference.StartsWith("#/components/schemas/", StringComparison.Ordinal))
            .Select(reference => reference["#/components/schemas/".Length..])
            .Where(name => !declared.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(dangling);
    }

    private static IEnumerable<string> References(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("$ref") && property.Value.ValueKind == JsonValueKind.String)
                        yield return property.Value.GetString()!;
                    foreach (var nested in References(property.Value))
                        yield return nested;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var nested in References(item))
                        yield return nested;
                break;
        }
    }

    private static JsonDocument LoadFrozenContract()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TestApp.slnx")))
            directory = directory.Parent;

        var path = Path.Combine(
            directory?.FullName ?? throw new Xunit.Sdk.XunitException("Не найден корень репозитория."),
            "docs/openapi/v1.json");

        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
