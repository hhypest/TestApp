namespace TestApp.Api;

internal static class ApiOperationContracts
{
    public static IReadOnlyDictionary<string, ApiOperationContract> All { get; } = Build();

    private static IReadOnlyDictionary<string, ApiOperationContract> Build()
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

    private static ApiOperationContract C(
        string method,
        string path,
        string operationId,
        string summary,
        string description) =>
        new(method, path, operationId, summary, description);
}

internal sealed record ApiOperationContract(
    string Method,
    string Path,
    string OperationId,
    string Summary,
    string Description);
