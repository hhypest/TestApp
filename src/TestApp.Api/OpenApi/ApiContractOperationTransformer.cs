using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TestApp.Api;

public sealed class ApiContractOperationTransformer : IOpenApiOperationTransformer
{
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var method = context.Description.HttpMethod?.ToUpperInvariant() ?? "HTTP";
        var path = NormalizePath(context.Description.RelativePath);
        var key = $"{method} {path}";

        if (ApiOperationContracts.All.TryGetValue(key, out var contract))
        {
            operation.OperationId = contract.OperationId;
            operation.Summary = contract.Summary;
            operation.Description = contract.Description;

            // Форма успешного ответа — часть контракта наравне с кодами ошибок. Эндпоинты
            // возвращают нетипизированный IResult (Results.Ok/ApiResultMapper), поэтому вывести
            // её из делегата нечем, и она объявляется здесь. Что ни один 200 не остался без
            // схемы, проверяет OpenApiResponseSchemaTests.
            var successSchema = await context.GetOrCreateSchemaAsync(contract.SuccessType, null, cancellationToken);
            AddSuccessResponse(operation, successSchema);
        }
        else
        {
            operation.OperationId ??= CreateFallbackOperationId(method, path);
            operation.Summary ??= $"{method} {path}";
        }

        if (IsAuthorized(context))
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            });
        }

        if (RequiresIfMatch(method, path))
            AddHeaderParameter(operation, IfMatchParameter());
        if (SupportsIdempotencyKey(method, path))
            AddHeaderParameter(operation, IdempotencyKeyParameter());

        var problemSchema = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), null, cancellationToken);
        AddProblemResponse(operation, "400", "Invalid request or validation failure.", problemSchema);
        if (IsAuthorized(context))
        {
            AddProblemResponse(operation, "401", "Authentication is required.", problemSchema);
            AddProblemResponse(operation, "403", "The authenticated principal is not allowed to perform the operation.", problemSchema);
        }
        AddProblemResponse(operation, "429", "The request was rejected by rate limiting.", problemSchema);

        if (MayReturnNotFound(method, path))
            AddProblemResponse(operation, "404", "The requested resource was not found or is not visible to the caller.", problemSchema);
        if (MayReturnConflict(method, path))
            AddProblemResponse(operation, "409", "The operation conflicts with current resource state or idempotency state.", problemSchema);
        if (RequiresIfMatch(method, path))
        {
            AddProblemResponse(operation, "412", "The supplied If-Match validator is stale.", problemSchema);
            AddProblemResponse(operation, "428", "A strong If-Match validator is required.", problemSchema);
        }

        if (method == "GET" && path == "/api/v1/tests/{id}/editor")
            AddEtagResponseHeader(operation);
    }

    private static bool IsAuthorized(OpenApiOperationTransformerContext context)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
            return false;
        return metadata.OfType<IAuthorizeData>().Any();
    }

    private static void AddHeaderParameter(OpenApiOperation operation, OpenApiParameter parameter)
    {
        operation.Parameters ??= [];
        if (operation.Parameters.Any(existing =>
                existing.In == ParameterLocation.Header &&
                string.Equals(existing.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
            return;
        operation.Parameters.Add(parameter);
    }

    private static OpenApiParameter IfMatchParameter() => new()
    {
        Name = "If-Match",
        In = ParameterLocation.Header,
        Required = true,
        Description = "Strong ETag returned by GET /api/v1/tests/{id}/editor. Weak validators and '*' are rejected.",
        Schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Pattern = "^\\\"[0-9]+\\\"$"
        },
        Example = JsonValue.Create("\"7\"")
    };

    private static OpenApiParameter IdempotencyKeyParameter() => new()
    {
        Name = "Idempotency-Key",
        In = ParameterLocation.Header,
        Required = false,
        Description = "Preferred UUID retry key. The legacy body idempotencyKey remains temporarily supported; if both are supplied they must match.",
        Schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Format = "uuid"
        },
        Example = JsonValue.Create("018f42d7-55b7-7b66-bdb6-7f0b1ef00c01")
    };

    private static void AddSuccessResponse(OpenApiOperation operation, IOpenApiSchema successSchema)
    {
        operation.Responses ??= new OpenApiResponses();
        operation.Responses["200"] = new OpenApiResponse
        {
            Description = "OK",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = successSchema }
            }
        };
    }

    private static void AddProblemResponse(
        OpenApiOperation operation,
        string statusCode,
        string description,
        IOpenApiSchema problemSchema)
    {
        operation.Responses ??= new OpenApiResponses();
        if (operation.Responses.ContainsKey(statusCode))
            return;

        operation.Responses[statusCode] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new OpenApiMediaType
                {
                    Schema = problemSchema
                }
            }
        };
    }

    private static void AddEtagResponseHeader(OpenApiOperation operation)
    {
        if (operation.Responses is null ||
            !operation.Responses.TryGetValue("200", out var okResponse) ||
            okResponse is not OpenApiResponse mutableResponse)
            return;

        mutableResponse.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.OrdinalIgnoreCase);
        mutableResponse.Headers["ETag"] = new OpenApiHeader
        {
            Description = "Strong numeric representation of the current Test.ConcurrencyVersion.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Pattern = "^\\\"[0-9]+\\\"$"
            }
        };
    }

    private static bool RequiresIfMatch(string method, string path) =>
        path.StartsWith("/api/v1/tests/{id}", StringComparison.Ordinal) &&
        method is "POST" or "PUT" or "PATCH" or "DELETE" &&
        path != "/api/v1/tests/{id}/editor" &&
        path != "/api/v1/tests/{id}/revisions";

    private static bool SupportsIdempotencyKey(string method, string path) =>
        method == "POST" && path is
            "/api/v1/tests/{id}/publish" or
            "/api/v1/assignments" or
            "/api/v1/assignments/bulk" or
            "/api/v1/assignments/{id}/attempts" or
            "/api/v1/attempts/{id}/submit";

    private static bool MayReturnNotFound(string method, string path) =>
        path.Contains("{id}", StringComparison.Ordinal) ||
        path.Contains("{attemptId}", StringComparison.Ordinal) ||
        path.Contains("{questionId}", StringComparison.Ordinal) ||
        path.Contains("{optionId}", StringComparison.Ordinal) ||
        path.Contains("{eventId}", StringComparison.Ordinal);

    private static bool MayReturnConflict(string method, string path) =>
        method is "POST" or "PUT" or "PATCH" or "DELETE";

    private static string NormalizePath(string? relativePath)
    {
        var path = (relativePath ?? string.Empty).Split('?', 2)[0].Trim('/');
        return "/" + path;
    }

    private static string CreateFallbackOperationId(string method, string path)
    {
        var sanitized = new string(path.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray())
            .Trim('_');
        return $"{method}_{sanitized}";
    }
}
