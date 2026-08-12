using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TestApp.Api;

public sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (!authenticationSchemes.Any(scheme => string.Equals(scheme.Name, "Bearer", StringComparison.Ordinal)))
            return;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "JWT",
                Description = "Keycloak access token."
            }
        };
    }
}

public sealed class ApiContractOperationTransformer : IOpenApiOperationTransformer
{
    private static readonly IReadOnlyDictionary<string, OperationContract> Contracts = BuildContracts();

    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var method = context.Description.HttpMethod?.ToUpperInvariant() ?? "HTTP";
        var path = NormalizePath(context.Description.RelativePath);
        var key = $"{method} {path}";

        if (Contracts.TryGetValue(key, out var contract))
        {
            operation.OperationId = contract.OperationId;
            operation.Summary = contract.Summary;
            operation.Description = contract.Description;
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

    private static IReadOnlyDictionary<string, OperationContract> BuildContracts()
    {
        var items = new[]
        {
            C("GET", "/api/v1/tests", "Tests_List", "List tests", "Returns the owner-scoped test catalog for authors and the global catalog for administrators."),
            C("POST", "/api/v1/tests", "Tests_Create", "Create test", "Creates a new draft test owned by the current external user."),
            C("PATCH", "/api/v1/tests/{id}/title", "Tests_Rename", "Rename test", "Changes the working test title using optimistic concurrency."),
            C("PATCH", "/api/v1/tests/{id}/settings", "Tests_ChangeSettings", "Change test settings", "Changes passing percentage and optional time limit using optimistic concurrency."),
            C("POST", "/api/v1/tests/{id}/questions", "Tests_AddQuestion", "Add question", "Adds a choice question to the mutable working test."),
            C("PUT", "/api/v1/tests/{id}/questions/{questionId}", "Tests_UpdateQuestion", "Update question", "Updates question text, type and points."),
            C("DELETE", "/api/v1/tests/{id}/questions/{questionId}", "Tests_RemoveQuestion", "Remove question", "Removes a question from the working test."),
            C("PATCH", "/api/v1/tests/{id}/questions/{questionId}/order", "Tests_ReorderQuestion", "Reorder question", "Changes a question order within the working test."),
            C("POST", "/api/v1/tests/{id}/questions/{questionId}/options", "Tests_AddOption", "Add answer option", "Adds an answer option to a question."),
            C("PUT", "/api/v1/tests/{id}/questions/{questionId}/options/{optionId}", "Tests_UpdateOption", "Update answer option", "Updates answer option text and correctness."),
            C("DELETE", "/api/v1/tests/{id}/questions/{questionId}/options/{optionId}", "Tests_RemoveOption", "Remove answer option", "Removes an answer option from a question."),
            C("PATCH", "/api/v1/tests/{id}/questions/{questionId}/options/{optionId}/order", "Tests_ReorderOption", "Reorder answer option", "Changes answer option order within a question."),
            C("POST", "/api/v1/tests/{id}/publish", "Tests_Publish", "Publish test", "Creates an immutable published revision. Supports Idempotency-Key replay and requires If-Match."),
            C("POST", "/api/v1/tests/{id}/archive", "Tests_Archive", "Archive test", "Archives the mutable test definition."),
            C("GET", "/api/v1/tests/{id}/editor", "Tests_GetEditor", "Get editor view", "Returns author-only mutable test data including correctness and the current strong ETag."),
            C("GET", "/api/v1/tests/{id}/revisions", "Tests_ListRevisions", "List published revisions", "Returns immutable revision metadata for a test."),
            C("GET", "/api/v1/assignments", "Assignments_List", "List assignments", "Returns administrator assignment summaries and attempt statistics."),
            C("GET", "/api/v1/assignments/{id}", "Assignments_Get", "Get assignment", "Returns administrator assignment details."),
            C("GET", "/api/v1/assignments/{id}/attempts", "Assignments_ListAttempts", "List assignment attempts", "Returns attempts associated with an assignment."),
            C("POST", "/api/v1/assignments", "Assignments_Create", "Create assignment", "Assigns an immutable published revision to one user or group. Supports Idempotency-Key replay."),
            C("POST", "/api/v1/assignments/bulk", "Assignments_BulkCreate", "Create assignments in bulk", "Creates assignments for a validated set of users/groups with one idempotency key."),
            C("PATCH", "/api/v1/assignments/{id}/window", "Assignments_ChangeWindow", "Change assignment window", "Changes availability bounds of an active assignment."),
            C("PATCH", "/api/v1/assignments/{id}/attempt-limit", "Assignments_ChangeAttemptLimit", "Change attempt limit", "Changes or removes the attempt limit of an active assignment."),
            C("POST", "/api/v1/assignments/{id}/cancel", "Assignments_Cancel", "Cancel assignment", "Cancels an active assignment with optional audit reason."),
            C("POST", "/api/v1/assignments/{id}/attempts", "Attempts_Start", "Start attempt", "Starts an attempt for the current assignment target. Supports Idempotency-Key replay."),
            C("GET", "/api/v1/me/assignments", "Me_ListAssignments", "List my assignments", "Returns assignments visible to the current user through direct or group targeting."),
            C("GET", "/api/v1/me/attempts", "Me_ListAttempts", "List my attempts", "Returns attempts owned by the current user."),
            C("GET", "/api/v1/results", "Results_List", "List reviewer results", "Returns owner-scoped reviewer results for authors and global results for administrators."),
            C("GET", "/api/v1/results/{attemptId}", "Results_Get", "Get reviewer result", "Returns reviewer-only correctness breakdown for one attempt."),
            C("GET", "/api/v1/operations/outbox", "Operations_GetOutbox", "Get Outbox status", "Returns safe operational delivery metadata without event payloads."),
            C("GET", "/api/v1/operations/outbox/dead-letters/{eventId}", "OutboxDeadLetters_Get", "Get Outbox dead letter", "Returns safe administrator-only dead-letter metadata without the event payload."),
            C("POST", "/api/v1/operations/outbox/dead-letters/{eventId}/requeue", "OutboxDeadLetters_Requeue", "Requeue Outbox dead letter", "Requeues an active dead letter after an explicit administrator reason and records an immutable management audit action."),
            C("POST", "/api/v1/operations/outbox/dead-letters/{eventId}/discard", "OutboxDeadLetters_Discard", "Discard Outbox dead letter", "Marks an active dead letter as terminally discarded without deleting its operational record and records an immutable management audit action."),
            C("GET", "/api/v1/operations/audit", "Operations_GetAudit", "Query audit trail", "Returns paged state-changing request audit metadata."),
            C("PUT", "/api/v1/attempts/{id}/answers/{questionId}", "Attempts_Answer", "Save answer", "Stores selected option identifiers for one question in an in-progress owned attempt."),
            C("DELETE", "/api/v1/attempts/{id}/answers/{questionId}", "Attempts_ClearAnswer", "Clear answer", "Removes the saved response for one question."),
            C("POST", "/api/v1/attempts/{id}/submit", "Attempts_Submit", "Submit attempt", "Completes and scores an attempt. Supports Idempotency-Key replay."),
            C("POST", "/api/v1/attempts/{id}/timeout", "Attempts_Timeout", "Timeout attempt", "Administrator command that completes an attempt as timed out."),
            C("GET", "/api/v1/attempts/{id}", "Attempts_Get", "Get attempt", "Returns the student-safe attempt view without correct-answer flags."),
            C("GET", "/api/v1/attempts/{id}/result", "Attempts_GetResult", "Get own result", "Returns the student-safe final result without correctness breakdown.")
        };

        return items.ToDictionary(item => $"{item.Method} {item.Path}", StringComparer.Ordinal);
    }

    private static OperationContract C(string method, string path, string operationId, string summary, string description) =>
        new(method, path, operationId, summary, description);

    private sealed record OperationContract(string Method, string Path, string OperationId, string Summary, string Description);
}

public sealed class ApiExampleSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        switch (context.JsonTypeInfo.Type.Name)
        {
            case "CreateTestRequest":
                schema.Example = JsonNode.Parse("""{"title":"DDD fundamentals"}""");
                break;
            case "QuestionWriteRequest":
                schema.Example = JsonNode.Parse("""{"text":"What is an aggregate?","type":1,"points":1,"order":1}""");
                break;
            case "AnswerOptionWriteRequest":
                schema.Example = JsonNode.Parse("""{"text":"Consistency boundary","isCorrect":true,"order":1}""");
                break;
            case "AssignRequest":
                schema.Example = JsonNode.Parse("""{"revisionId":"018f42d7-55b7-7b66-bdb6-7f0b1ef00c01","userId":"student-sub","groupId":null,"availableFrom":"2026-08-12T12:00:00Z","availableUntil":"2026-08-13T12:00:00Z","attemptLimit":1,"idempotencyKey":"00000000-0000-0000-0000-000000000000"}""");
                break;
            case "AnswerQuestionRequest":
                schema.Example = JsonNode.Parse("""{"optionIds":["018f42d7-55b7-7b66-bdb6-7f0b1ef00c01"]}""");
                break;
            case "DeadLetterActionRequest":
                schema.Example = JsonNode.Parse("""{"reason":"Dependency fixed; retry delivery."}""");
                break;
            case "QuestionType":
                schema.Description = "Question type. 1 = SingleChoice, 2 = MultipleChoice.";
                break;
            case "TestStatus":
                schema.Description = "Test lifecycle status. 0 = Draft, 1 = Published, 2 = Archived.";
                break;
            case "AssignmentStatus":
                schema.Description = "Assignment status. 1 = Active, 2 = Cancelled.";
                break;
            case "AttemptStatus":
                schema.Description = "Attempt lifecycle status: InProgress, Submitted or TimedOut as defined by the API enum schema.";
                break;
            case "AttemptOutcome":
                schema.Description = "Final attempt outcome: Passed or Failed.";
                break;
        }

        return Task.CompletedTask;
    }
}