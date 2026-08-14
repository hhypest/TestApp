using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TestApp.Api;

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
