using TestApp.Api;
using TestApp.Application.Queries;
using TestApp.Application.Tests;
using TestApp.Domain.Tests;

namespace TestApp.Api.Endpoints;

internal static class TestEndpoints
{
    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tests = endpoints.MapGroup("/api/v1/tests")
            .RequireAuthorization()
            .AddEndpointFilter<RequestValidationFilter>();

        tests.MapGet("/", async (int? page, int? pageSize, TestStatus? status, string? search, GetTestsQueryHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new GetTestsQuery(page ?? 1, pageSize ?? 20, status, search), ct)))
            .RequireAuthorization(Permissions.TestsWrite)
            .RequireRateLimiting(RatePolicies.PrivilegedRead);

        tests.MapPost("/", async (CreateTestRequest request, CreateTestCommandHandler handler, CancellationToken ct) =>
            ApiResultMapper.ToHttp(await handler.Handle(new CreateTestCommand(request.Title), ct)))
            .RequireAuthorization(Permissions.TestsWrite);

        tests.MapPatch("/{id}/title", async (Guid id, HttpRequest http, RenameTestRequest request, RenameTestCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            return version.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new RenameTestCommand(new TestId(id), request.Title, version.Value), ct))
                : version.Error!;
        }).RequireAuthorization(Permissions.TestsWrite);

        tests.MapPatch("/{id}/settings", async (Guid id, HttpRequest http, TestSettingsRequest request, ChangeTestSettingsCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            return version.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new ChangeTestSettingsCommand(new TestId(id), request.PassingPercentage, request.TimeLimitMinutes, version.Value), ct))
                : version.Error!;
        }).RequireAuthorization(Permissions.TestsWrite);

        tests.MapPost("/{id}/questions", async (Guid id, HttpRequest http, QuestionWriteRequest request, AddQuestionCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            return version.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new AddQuestionCommand(new TestId(id), request.Text, request.Type, request.Points, request.Order, version.Value), ct))
                : version.Error!;
        }).RequireAuthorization(Permissions.TestsWrite);

        tests.MapPut("/{id}/questions/{questionId}", async (Guid id, Guid questionId, HttpRequest http, QuestionUpdateRequest request, UpdateQuestionCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            return version.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new UpdateQuestionCommand(new TestId(id), new QuestionId(questionId), request.Text, request.Type, request.Points, version.Value), ct))
                : version.Error!;
        }).RequireAuthorization(Permissions.TestsWrite);

        tests.MapDelete("/{id}/questions/{questionId}", async (Guid id, Guid questionId, HttpRequest http, RemoveQuestionCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            return version.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new RemoveQuestionCommand(new TestId(id), new QuestionId(questionId), version.Value), ct))
                : version.Error!;
        }).RequireAuthorization(Permissions.TestsWrite);

        tests.MapPatch("/{id}/questions/{questionId}/order", async (Guid id, Guid questionId, HttpRequest http, OrderRequest request, ReorderQuestionCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            return version.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new ReorderQuestionCommand(new TestId(id), new QuestionId(questionId), request.Order, version.Value), ct))
                : version.Error!;
        }).RequireAuthorization(Permissions.TestsWrite);

        tests.MapPost("/{id}/questions/{questionId}/options", async (Guid id, Guid questionId, HttpRequest http, AnswerOptionWriteRequest request, AddAnswerOptionCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            return version.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new AddAnswerOptionCommand(new TestId(id), new QuestionId(questionId), request.Text, request.IsCorrect, request.Order, version.Value), ct))
                : version.Error!;
        }).RequireAuthorization(Permissions.TestsWrite);

        tests.MapPut("/{id}/questions/{questionId}/options/{optionId}", async (Guid id, Guid questionId, Guid optionId, HttpRequest http, AnswerOptionUpdateRequest request, UpdateAnswerOptionCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            return version.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new UpdateAnswerOptionCommand(new TestId(id), new QuestionId(questionId), new AnswerOptionId(optionId), request.Text, request.IsCorrect, version.Value), ct))
                : version.Error!;
        }).RequireAuthorization(Permissions.TestsWrite);

        tests.MapDelete("/{id}/questions/{questionId}/options/{optionId}", async (Guid id, Guid questionId, Guid optionId, HttpRequest http, RemoveAnswerOptionCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            return version.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new RemoveAnswerOptionCommand(new TestId(id), new QuestionId(questionId), new AnswerOptionId(optionId), version.Value), ct))
                : version.Error!;
        }).RequireAuthorization(Permissions.TestsWrite);

        tests.MapPatch("/{id}/questions/{questionId}/options/{optionId}/order", async (Guid id, Guid questionId, Guid optionId, HttpRequest http, OrderRequest request, ReorderAnswerOptionCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            return version.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new ReorderAnswerOptionCommand(new TestId(id), new QuestionId(questionId), new AnswerOptionId(optionId), request.Order, version.Value), ct))
                : version.Error!;
        }).RequireAuthorization(Permissions.TestsWrite);

        tests.MapPost("/{id}/publish", async (Guid id, HttpRequest http, PublishRequest request, PublishTestCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            if (!version.IsSuccess)
                return version.Error!;

            var key = IdempotencyKeyResolver.Resolve(http, request.IdempotencyKey);
            return key.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new PublishTestCommand(new TestId(id), key.Value, version.Value), ct))
                : key.Error!;
        }).RequireAuthorization(Permissions.TestsPublish);

        tests.MapPost("/{id}/archive", async (Guid id, HttpRequest http, ArchiveTestCommandHandler handler, CancellationToken ct) =>
        {
            var version = TestEtags.ResolveRequiredIfMatch(http);
            return version.IsSuccess
                ? ApiResultMapper.ToHttp(await handler.Handle(new ArchiveTestCommand(new TestId(id), version.Value), ct))
                : version.Error!;
        }).RequireAuthorization(Permissions.TestsWrite);

        tests.MapGet("/{id}/editor", async (Guid id, HttpResponse response, GetTestEditorViewQueryHandler handler, CancellationToken ct) =>
        {
            var value = await handler.Handle(new GetTestEditorViewQuery(new TestId(id)), ct);
            if (value is null)
                return Results.NotFound();

            response.Headers.ETag = TestEtags.Format(value.ConcurrencyVersion);
            return Results.Ok(value);
        }).RequireAuthorization(Permissions.TestsWrite).RequireRateLimiting(RatePolicies.PrivilegedRead);

        tests.MapGet("/{id}/revisions", async (Guid id, GetTestRevisionsQueryHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new GetTestRevisionsQuery(new TestId(id)), ct)))
            .RequireAuthorization(Permissions.TestsWrite)
            .RequireRateLimiting(RatePolicies.PrivilegedRead);

        return endpoints;
    }
}
