using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TestApp.Api;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task OpenApi_documents_security_retry_concurrency_and_error_contracts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("openapi", out var openApiVersion));
        Assert.StartsWith("3.", openApiVersion.GetString(), StringComparison.Ordinal);

        var paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/tests", out var testsPath));
        Assert.False(paths.TryGetProperty("/api/tests", out _));

        var listTests = testsPath.GetProperty("get");
        Assert.Equal("Tests_List", listTests.GetProperty("operationId").GetString());
        Assert.Equal("List tests", listTests.GetProperty("summary").GetString());
        Assert.False(string.IsNullOrWhiteSpace(listTests.GetProperty("description").GetString()));
        AssertBearerSecurity(listTests);

        var securitySchemes = root.GetProperty("components").GetProperty("securitySchemes");
        var bearer = securitySchemes.GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var publish = paths.GetProperty("/api/v1/tests/{id}/publish").GetProperty("post");
        Assert.Equal("Tests_Publish", publish.GetProperty("operationId").GetString());
        AssertBearerSecurity(publish);
        AssertHeaderParameter(publish, "If-Match", required: true);
        AssertHeaderParameter(publish, "Idempotency-Key", required: false);
        AssertResponses(publish, "400", "401", "403", "404", "409", "412", "428", "429");

        var problemResponse = publish.GetProperty("responses").GetProperty("400");
        var problemContent = problemResponse.GetProperty("content");
        Assert.True(problemContent.TryGetProperty("application/problem+json", out var problemMediaType));
        Assert.True(problemMediaType.TryGetProperty("schema", out _));

        var editor = paths.GetProperty("/api/v1/tests/{id}/editor").GetProperty("get");
        Assert.Equal("Tests_GetEditor", editor.GetProperty("operationId").GetString());
        var editorHeaders = editor.GetProperty("responses").GetProperty("200").GetProperty("headers");
        Assert.True(editorHeaders.TryGetProperty("ETag", out var etag));
        Assert.False(string.IsNullOrWhiteSpace(etag.GetProperty("description").GetString()));

        var assignment = paths.GetProperty("/api/v1/assignments").GetProperty("post");
        Assert.Equal("Assignments_Create", assignment.GetProperty("operationId").GetString());
        AssertHeaderParameter(assignment, "Idempotency-Key", required: false);
        AssertBearerSecurity(assignment);

        var deadLetterPath = paths.GetProperty("/api/v1/operations/outbox/dead-letters/{eventId}");
        var deadLetterDetail = deadLetterPath.GetProperty("get");
        Assert.Equal("OutboxDeadLetters_Get", deadLetterDetail.GetProperty("operationId").GetString());
        Assert.Equal("Get Outbox dead letter", deadLetterDetail.GetProperty("summary").GetString());
        AssertBearerSecurity(deadLetterDetail);
        AssertResponses(deadLetterDetail, "400", "401", "403", "404", "429");

        var requeue = paths.GetProperty("/api/v1/operations/outbox/dead-letters/{eventId}/requeue").GetProperty("post");
        Assert.Equal("OutboxDeadLetters_Requeue", requeue.GetProperty("operationId").GetString());
        AssertBearerSecurity(requeue);
        AssertResponses(requeue, "400", "401", "403", "404", "409", "429");

        var discard = paths.GetProperty("/api/v1/operations/outbox/dead-letters/{eventId}/discard").GetProperty("post");
        Assert.Equal("OutboxDeadLetters_Discard", discard.GetProperty("operationId").GetString());
        AssertBearerSecurity(discard);
        AssertResponses(discard, "400", "401", "403", "404", "409", "429");

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var createTestSchema = FindSchema(schemas, "CreateTestRequest");
        Assert.True(createTestSchema.TryGetProperty("example", out var createExample));
        Assert.Equal("DDD fundamentals", createExample.GetProperty("title").GetString());

        var deadLetterActionSchema = FindSchema(schemas, "DeadLetterActionRequest");
        Assert.True(deadLetterActionSchema.TryGetProperty("example", out var deadLetterExample));
        Assert.False(string.IsNullOrWhiteSpace(deadLetterExample.GetProperty("reason").GetString()));
    }

    private static void AssertBearerSecurity(JsonElement operation)
    {
        var security = operation.GetProperty("security");
        Assert.Contains(security.EnumerateArray(), requirement => requirement.TryGetProperty("Bearer", out _));
    }

    private static void AssertHeaderParameter(JsonElement operation, string name, bool required)
    {
        var parameter = Assert.Single(
            operation.GetProperty("parameters").EnumerateArray(),
            candidate =>
                string.Equals(candidate.GetProperty("in").GetString(), "header", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase));

        var actualRequired = parameter.TryGetProperty("required", out var requiredProperty) && requiredProperty.GetBoolean();
        Assert.Equal(required, actualRequired);
        Assert.False(string.IsNullOrWhiteSpace(parameter.GetProperty("description").GetString()));
    }

    private static void AssertResponses(JsonElement operation, params string[] expectedStatusCodes)
    {
        var responses = operation.GetProperty("responses");
        foreach (var statusCode in expectedStatusCodes)
            Assert.True(responses.TryGetProperty(statusCode, out _), $"Expected OpenAPI response {statusCode}.");
    }

    private static JsonElement FindSchema(JsonElement schemas, string suffix)
    {
        foreach (var schema in schemas.EnumerateObject())
        {
            if (schema.Name.EndsWith(suffix, StringComparison.Ordinal))
                return schema.Value;
        }

        throw new Xunit.Sdk.XunitException($"OpenAPI schema ending with '{suffix}' was not found.");
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        ApiTestHost.Create(connectionString, useTestAuthentication: false);
}
