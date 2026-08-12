using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TestApp.Api;
using TestApp.Application.Assignments;
using TestApp.Application.Attempts;
using TestApp.Application.Common;
using TestApp.Application.Queries;
using TestApp.Application.Tests;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Infrastructure;
using TestApp.Infrastructure.Attempts;
using TestApp.Infrastructure.Outbox;
using TestApp.Infrastructure.Persistence;

var migrateOnly = args.Any(x => string.Equals(x, "--migrate", StringComparison.OrdinalIgnoreCase));
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

var databaseOptions = RuntimeConfiguration.LoadDatabase(builder.Configuration, builder.Environment);
var keycloakOptions = migrateOnly ? null : RuntimeConfiguration.LoadKeycloak(builder.Configuration, builder.Environment);
var rabbitMqOptions = RuntimeConfiguration.LoadRabbitMq(builder.Configuration);
var expirationOptions = RuntimeConfiguration.LoadAttemptExpiration(builder.Configuration);
var rateLimitingOptions = RuntimeConfiguration.LoadRateLimiting(builder.Configuration);
var openApiOptions = RuntimeConfiguration.LoadOpenApi(builder.Configuration, builder.Environment);
var corsOptions = RuntimeConfiguration.LoadCors(builder.Configuration);
var transportSecurityOptions = migrateOnly
    ? null
    : RuntimeConfiguration.LoadTransportSecurity(builder.Configuration, builder.Environment);
var reverseProxyOptions = RuntimeConfiguration.LoadReverseProxy(builder.Configuration);

RuntimeConfiguration.RegisterTypedOptions(
    builder.Services,
    databaseOptions,
    keycloakOptions,
    rabbitMqOptions,
    expirationOptions,
    rateLimitingOptions,
    openApiOptions,
    corsOptions,
    transportSecurityOptions,
    reverseProxyOptions);
RuntimeConfiguration.ConfigureForwardedHeaders(builder.Services, reverseProxyOptions);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

if (!migrateOnly)
{
    builder.Services.AddOpenApi();
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
    {
        o.Authority = keycloakOptions!.Authority;
        o.Audience = keycloakOptions.Audience;
        o.RequireHttpsMetadata = keycloakOptions.RequireHttpsMetadata;
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "sub",
            RoleClaimType = "roles"
        };
    });

    builder.Services.AddAuthorization(o =>
    {
        o.AddPolicy(Permissions.TestsWrite, p => p.RequireRole("test-author", "test-admin"));
        o.AddPolicy(Permissions.TestsPublish, p => p.RequireRole("test-author", "test-admin"));
        o.AddPolicy(Permissions.TestsAssign, p => p.RequireRole("test-admin"));
        o.AddPolicy(Permissions.ResultsReview, p => p.RequireRole("test-author", "test-admin"));
        o.AddPolicy(Permissions.OperationsRead, p => p.RequireRole("test-admin"));
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            FixedWindowPartition(context, rateLimitingOptions.General));
        options.AddPolicy(RatePolicies.StudentWrite, context =>
            FixedWindowPartition(context, rateLimitingOptions.StudentWrite));
        options.AddPolicy(RatePolicies.PrivilegedRead, context =>
            FixedWindowPartition(context, rateLimitingOptions.PrivilegedRead));
        options.AddPolicy(RatePolicies.Operations, context =>
            FixedWindowPartition(context, rateLimitingOptions.Operations));
    });

    if (corsOptions.Enabled)
    {
        builder.Services.AddCors(options => options.AddPolicy(CorsPolicies.Api, policy =>
        {
            policy.WithOrigins(corsOptions.AllowedOrigins.ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod();
            if (corsOptions.AllowCredentials)
                policy.AllowCredentials();
        }));
    }

    if (transportSecurityOptions!.HstsEnabled)
    {
        builder.Services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(transportSecurityOptions.HstsMaxAgeDays);
            options.IncludeSubDomains = transportSecurityOptions.HstsIncludeSubDomains;
            options.Preload = transportSecurityOptions.HstsPreload;
        });
    }
}

builder.Services.AddInfrastructure(o =>
    o.UseMySql(databaseOptions.ConnectionString, ServerVersion.AutoDetect(databaseOptions.ConnectionString)));
builder.Services.Configure<AttemptExpirationOptions>(options =>
{
    options.BatchSize = expirationOptions.BatchSize;
    options.PollInterval = TimeSpan.FromSeconds(expirationOptions.PollIntervalSeconds);
});

if (!migrateOnly && rabbitMqOptions.Enabled)
{
    builder.Services.AddRabbitMqOutboxDelivery(
        options =>
        {
            options.Enabled = true;
            options.ConnectionString = rabbitMqOptions.ConnectionString;
            options.Exchange = rabbitMqOptions.Exchange;
            options.RoutingKeyPrefix = rabbitMqOptions.RoutingKeyPrefix;
            options.ClientProvidedName = rabbitMqOptions.ClientProvidedName;
        },
        options =>
        {
            options.BatchSize = rabbitMqOptions.BatchSize;
            options.MaxAttempts = rabbitMqOptions.MaxAttempts;
            options.PollInterval = TimeSpan.FromSeconds(rabbitMqOptions.PollIntervalSeconds);
            options.BaseRetryDelay = TimeSpan.FromSeconds(rabbitMqOptions.BaseRetryDelaySeconds);
            options.MaxRetryDelay = TimeSpan.FromSeconds(rabbitMqOptions.MaxRetryDelaySeconds);
            options.AdvisoryLockTimeoutSeconds = rabbitMqOptions.AdvisoryLockTimeoutSeconds;
        });
}

builder.Services.AddScoped<CreateTestCommandHandler>();
builder.Services.AddScoped<RenameTestCommandHandler>();
builder.Services.AddScoped<ChangeTestSettingsCommandHandler>();
builder.Services.AddScoped<AddQuestionCommandHandler>();
builder.Services.AddScoped<UpdateQuestionCommandHandler>();
builder.Services.AddScoped<RemoveQuestionCommandHandler>();
builder.Services.AddScoped<ReorderQuestionCommandHandler>();
builder.Services.AddScoped<AddAnswerOptionCommandHandler>();
builder.Services.AddScoped<UpdateAnswerOptionCommandHandler>();
builder.Services.AddScoped<RemoveAnswerOptionCommandHandler>();
builder.Services.AddScoped<ReorderAnswerOptionCommandHandler>();
builder.Services.AddScoped<ArchiveTestCommandHandler>();
builder.Services.AddScoped<PublishTestCommandHandler>();
builder.Services.AddScoped<AssignTestCommandHandler>();
builder.Services.AddScoped<BulkAssignTestsCommandHandler>();
builder.Services.AddScoped<CancelAssignmentCommandHandler>();
builder.Services.AddScoped<ChangeAssignmentWindowCommandHandler>();
builder.Services.AddScoped<ChangeAssignmentAttemptLimitCommandHandler>();
builder.Services.AddScoped<StartAttemptCommandHandler>();
builder.Services.AddScoped<AnswerQuestionCommandHandler>();
builder.Services.AddScoped<ClearAnswerCommandHandler>();
builder.Services.AddScoped<SubmitAttemptCommandHandler>();
builder.Services.AddScoped<TimeoutAttemptCommandHandler>();
builder.Services.AddScoped<GetTestsQueryHandler>();
builder.Services.AddScoped<GetTestRevisionsQueryHandler>();
builder.Services.AddScoped<GetTestEditorViewQueryHandler>();
builder.Services.AddScoped<GetAdminAssignmentsQueryHandler>();
builder.Services.AddScoped<GetAdminAssignmentQueryHandler>();
builder.Services.AddScoped<GetAdminAssignmentAttemptsQueryHandler>();
builder.Services.AddScoped<GetMyAssignmentsQueryHandler>();
builder.Services.AddScoped<GetMyAttemptsQueryHandler>();
builder.Services.AddScoped<GetAttemptQueryHandler>();
builder.Services.AddScoped<GetAttemptResultQueryHandler>();
builder.Services.AddScoped<GetReviewerResultsQueryHandler>();
builder.Services.AddScoped<GetReviewerAttemptResultQueryHandler>();

var app = builder.Build();

if (migrateOnly || databaseOptions.ApplyMigrationsOnStartup)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

if (migrateOnly)
    return;

if (reverseProxyOptions.Enabled)
    app.UseForwardedHeaders();
if (transportSecurityOptions!.HstsEnabled)
    app.UseHsts();
if (transportSecurityOptions.HttpsRedirectionEnabled)
    app.UseHttpsRedirection();
if (transportSecurityOptions.SecurityHeadersEnabled)
    app.UseMiddleware<SecurityHeadersMiddleware>();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api", out var remaining) &&
        !context.Request.Path.StartsWithSegments("/api/v1"))
    {
        context.Request.Path = "/api/v1" + remaining;
    }

    await next();
});

app.UseRouting();
if (corsOptions.Enabled)
    app.UseCors(CorsPolicies.Api);
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseMiddleware<CorrelationAuditMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

if (openApiOptions.Enabled)
{
    var openApiEndpoint = app.MapOpenApi("/openapi/v1.json");
    if (openApiOptions.AllowAnonymous)
        openApiEndpoint.AllowAnonymous();
    else
        openApiEndpoint.RequireAuthorization(Permissions.OperationsRead).RequireRateLimiting(RatePolicies.Operations);
}

var tests = app.MapGroup("/api/v1/tests").RequireAuthorization();
tests.MapGet("/", async (int? page, int? pageSize, TestStatus? status, string? search, GetTestsQueryHandler h, CancellationToken ct) =>
    Results.Ok(await h.Handle(new GetTestsQuery(page ?? 1, pageSize ?? 20, status, search), ct))).RequireAuthorization(Permissions.TestsWrite).RequireRateLimiting(RatePolicies.PrivilegedRead);
tests.MapPost("/", async (CreateTestRequest r, CreateTestCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new CreateTestCommand(r.Title), ct))).RequireAuthorization(Permissions.TestsWrite);
tests.MapPatch("/{id:guid}/title", async (Guid id, HttpRequest request, RenameTestRequest r, RenameTestCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    return version.IsSuccess ? ToHttp(await h.Handle(new RenameTestCommand(new TestId(id), r.Title, version.Value), ct)) : version.Error!;
}).RequireAuthorization(Permissions.TestsWrite);
tests.MapPatch("/{id:guid}/settings", async (Guid id, HttpRequest request, TestSettingsRequest r, ChangeTestSettingsCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    return version.IsSuccess ? ToHttp(await h.Handle(new ChangeTestSettingsCommand(new TestId(id), r.PassingPercentage, r.TimeLimitMinutes, version.Value), ct)) : version.Error!;
}).RequireAuthorization(Permissions.TestsWrite);
tests.MapPost("/{id:guid}/questions", async (Guid id, HttpRequest request, QuestionWriteRequest r, AddQuestionCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    return version.IsSuccess ? ToHttp(await h.Handle(new AddQuestionCommand(new TestId(id), r.Text, r.Type, r.Points, r.Order, version.Value), ct)) : version.Error!;
}).RequireAuthorization(Permissions.TestsWrite);
tests.MapPut("/{id:guid}/questions/{questionId:guid}", async (Guid id, Guid questionId, HttpRequest request, QuestionUpdateRequest r, UpdateQuestionCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    return version.IsSuccess ? ToHttp(await h.Handle(new UpdateQuestionCommand(new TestId(id), new QuestionId(questionId), r.Text, r.Type, r.Points, version.Value), ct)) : version.Error!;
}).RequireAuthorization(Permissions.TestsWrite);
tests.MapDelete("/{id:guid}/questions/{questionId:guid}", async (Guid id, Guid questionId, HttpRequest request, RemoveQuestionCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    return version.IsSuccess ? ToHttp(await h.Handle(new RemoveQuestionCommand(new TestId(id), new QuestionId(questionId), version.Value), ct)) : version.Error!;
}).RequireAuthorization(Permissions.TestsWrite);
tests.MapPatch("/{id:guid}/questions/{questionId:guid}/order", async (Guid id, Guid questionId, HttpRequest request, OrderRequest r, ReorderQuestionCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    return version.IsSuccess ? ToHttp(await h.Handle(new ReorderQuestionCommand(new TestId(id), new QuestionId(questionId), r.Order, version.Value), ct)) : version.Error!;
}).RequireAuthorization(Permissions.TestsWrite);
tests.MapPost("/{id:guid}/questions/{questionId:guid}/options", async (Guid id, Guid questionId, HttpRequest request, AnswerOptionWriteRequest r, AddAnswerOptionCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    return version.IsSuccess ? ToHttp(await h.Handle(new AddAnswerOptionCommand(new TestId(id), new QuestionId(questionId), r.Text, r.IsCorrect, r.Order, version.Value), ct)) : version.Error!;
}).RequireAuthorization(Permissions.TestsWrite);
tests.MapPut("/{id:guid}/questions/{questionId:guid}/options/{optionId:guid}", async (Guid id, Guid questionId, Guid optionId, HttpRequest request, AnswerOptionUpdateRequest r, UpdateAnswerOptionCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    return version.IsSuccess ? ToHttp(await h.Handle(new UpdateAnswerOptionCommand(new TestId(id), new QuestionId(questionId), new AnswerOptionId(optionId), r.Text, r.IsCorrect, version.Value), ct)) : version.Error!;
}).RequireAuthorization(Permissions.TestsWrite);
tests.MapDelete("/{id:guid}/questions/{questionId:guid}/options/{optionId:guid}", async (Guid id, Guid questionId, Guid optionId, HttpRequest request, RemoveAnswerOptionCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    return version.IsSuccess ? ToHttp(await h.Handle(new RemoveAnswerOptionCommand(new TestId(id), new QuestionId(questionId), new AnswerOptionId(optionId), version.Value), ct)) : version.Error!;
}).RequireAuthorization(Permissions.TestsWrite);
tests.MapPatch("/{id:guid}/questions/{questionId:guid}/options/{optionId:guid}/order", async (Guid id, Guid questionId, Guid optionId, HttpRequest request, OrderRequest r, ReorderAnswerOptionCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    return version.IsSuccess ? ToHttp(await h.Handle(new ReorderAnswerOptionCommand(new TestId(id), new QuestionId(questionId), new AnswerOptionId(optionId), r.Order, version.Value), ct)) : version.Error!;
}).RequireAuthorization(Permissions.TestsWrite);
tests.MapPost("/{id:guid}/publish", async (Guid id, HttpRequest request, PublishRequest r, PublishTestCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    if (!version.IsSuccess) return version.Error!;
    var key = IdempotencyKeyResolver.Resolve(request, r.IdempotencyKey);
    return key.IsSuccess
        ? ToHttp(await h.Handle(new PublishTestCommand(new TestId(id), key.Value, version.Value), ct))
        : key.Error!;
}).RequireAuthorization(Permissions.TestsPublish);
tests.MapPost("/{id:guid}/archive", async (Guid id, HttpRequest request, ArchiveTestCommandHandler h, CancellationToken ct) =>
{
    var version = TestEtags.ResolveRequiredIfMatch(request);
    return version.IsSuccess ? ToHttp(await h.Handle(new ArchiveTestCommand(new TestId(id), version.Value), ct)) : version.Error!;
}).RequireAuthorization(Permissions.TestsWrite);
tests.MapGet("/{id:guid}/editor", async (Guid id, HttpResponse response, GetTestEditorViewQueryHandler h, CancellationToken ct) =>
{
    var value = await h.Handle(new GetTestEditorViewQuery(new TestId(id)), ct);
    if (value is null) return Results.NotFound();
    response.Headers.ETag = TestEtags.Format(value.ConcurrencyVersion);
    return Results.Ok(value);
}).RequireAuthorization(Permissions.TestsWrite).RequireRateLimiting(RatePolicies.PrivilegedRead);
tests.MapGet("/{id:guid}/revisions", async (Guid id, GetTestRevisionsQueryHandler h, CancellationToken ct) =>
    Results.Ok(await h.Handle(new GetTestRevisionsQuery(new TestId(id)), ct))).RequireAuthorization(Permissions.TestsWrite).RequireRateLimiting(RatePolicies.PrivilegedRead);

var assignments = app.MapGroup("/api/v1/assignments").RequireAuthorization();
assignments.MapGet("/", async (Guid? testId, Guid? revisionId, AssignmentTargetType? targetType, string? targetId, AssignmentStatus? status, int? page, int? pageSize, GetAdminAssignmentsQueryHandler h, CancellationToken ct) =>
    Results.Ok(await h.Handle(new GetAdminAssignmentsQuery(
        testId is null ? null : new TestId(testId.Value),
        revisionId is null ? null : new PublishedTestRevisionId(revisionId.Value),
        targetType,
        targetId,
        status,
        page ?? 1,
        pageSize ?? 20), ct))).RequireAuthorization(Permissions.TestsAssign).RequireRateLimiting(RatePolicies.PrivilegedRead);
assignments.MapGet("/{id:guid}", async (Guid id, GetAdminAssignmentQueryHandler h, CancellationToken ct) =>
    await h.Handle(new GetAdminAssignmentQuery(new TestAssignmentId(id)), ct) is { } value ? Results.Ok(value) : Results.NotFound()).RequireAuthorization(Permissions.TestsAssign).RequireRateLimiting(RatePolicies.PrivilegedRead);
assignments.MapGet("/{id:guid}/attempts", async (Guid id, AttemptStatus? status, AttemptOutcome? outcome, int? page, int? pageSize, GetAdminAssignmentAttemptsQueryHandler h, CancellationToken ct) =>
    await h.Handle(new GetAdminAssignmentAttemptsQuery(new TestAssignmentId(id), status, outcome, page ?? 1, pageSize ?? 20), ct) is { } value ? Results.Ok(value) : Results.NotFound()).RequireAuthorization(Permissions.TestsAssign).RequireRateLimiting(RatePolicies.PrivilegedRead);
assignments.MapPost("/", async (HttpRequest request, AssignRequest r, AssignTestCommandHandler h, CancellationToken ct) =>
{
    var key = IdempotencyKeyResolver.Resolve(request, r.IdempotencyKey);
    if (!key.IsSuccess) return key.Error!;
    ExternalUserId? user = string.IsNullOrWhiteSpace(r.UserId) ? null : ExternalUserId.FromSubject(r.UserId);
    ExternalGroupId? group = string.IsNullOrWhiteSpace(r.GroupId) ? null : ExternalGroupId.FromExternalId(r.GroupId);
    return ToHttp(await h.Handle(new AssignTestCommand(new PublishedTestRevisionId(r.RevisionId), user, group, r.AvailableFrom, r.AvailableUntil, r.AttemptLimit, key.Value), ct));
}).RequireAuthorization(Permissions.TestsAssign);
assignments.MapPost("/bulk", async (HttpRequest request, BulkAssignRequest r, BulkAssignTestsCommandHandler h, CancellationToken ct) =>
{
    var key = IdempotencyKeyResolver.Resolve(request, r.IdempotencyKey);
    if (!key.IsSuccess) return key.Error!;
    var targets = r.Targets.Select(x => new BulkAssignmentTarget(
        string.IsNullOrWhiteSpace(x.UserId) ? null : ExternalUserId.FromSubject(x.UserId),
        string.IsNullOrWhiteSpace(x.GroupId) ? null : ExternalGroupId.FromExternalId(x.GroupId))).ToArray();
    return ToHttp(await h.Handle(new BulkAssignTestsCommand(
        new PublishedTestRevisionId(r.RevisionId),
        targets,
        r.AvailableFrom,
        r.AvailableUntil,
        r.AttemptLimit,
        key.Value), ct));
}).RequireAuthorization(Permissions.TestsAssign);
assignments.MapPatch("/{id:guid}/window", async (Guid id, AssignmentWindowRequest r, ChangeAssignmentWindowCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new ChangeAssignmentWindowCommand(new TestAssignmentId(id), r.AvailableFrom, r.AvailableUntil), ct))).RequireAuthorization(Permissions.TestsAssign);
assignments.MapPatch("/{id:guid}/attempt-limit", async (Guid id, AttemptLimitRequest r, ChangeAssignmentAttemptLimitCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new ChangeAssignmentAttemptLimitCommand(new TestAssignmentId(id), r.AttemptLimit), ct))).RequireAuthorization(Permissions.TestsAssign);
assignments.MapPost("/{id:guid}/cancel", async (Guid id, CancelAssignmentRequest r, CancelAssignmentCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new CancelAssignmentCommand(new TestAssignmentId(id), r.Reason), ct))).RequireAuthorization(Permissions.TestsAssign);
assignments.MapPost("/{id:guid}/attempts", async (Guid id, HttpRequest request, StartAttemptRequest r, StartAttemptCommandHandler h, CancellationToken ct) =>
{
    var key = IdempotencyKeyResolver.Resolve(request, r.IdempotencyKey);
    return key.IsSuccess
        ? ToHttp(await h.Handle(new StartAttemptCommand(new TestAssignmentId(id), key.Value), ct))
        : key.Error!;
}).RequireRateLimiting(RatePolicies.StudentWrite);

app.MapGet("/api/v1/me/assignments", async (int? page, int? pageSize, AssignmentStatus? status, GetMyAssignmentsQueryHandler h, CancellationToken ct) =>
    Results.Ok(await h.Handle(new GetMyAssignmentsQuery(page ?? 1, pageSize ?? 20, status), ct))).RequireAuthorization();
app.MapGet("/api/v1/me/attempts", async (int? page, int? pageSize, AttemptStatus? status, GetMyAttemptsQueryHandler h, CancellationToken ct) =>
    Results.Ok(await h.Handle(new GetMyAttemptsQuery(page ?? 1, pageSize ?? 20, status), ct))).RequireAuthorization();
app.MapGet("/api/v1/results", async (Guid? testId, Guid? revisionId, AttemptOutcome? outcome, int? page, int? pageSize, GetReviewerResultsQueryHandler h, CancellationToken ct) =>
    Results.Ok(await h.Handle(new GetReviewerResultsQuery(testId is null ? null : new TestId(testId.Value), revisionId is null ? null : new PublishedTestRevisionId(revisionId.Value), outcome, page ?? 1, pageSize ?? 20), ct))).RequireAuthorization(Permissions.ResultsReview).RequireRateLimiting(RatePolicies.PrivilegedRead);
app.MapGet("/api/v1/results/{attemptId:guid}", async (Guid attemptId, GetReviewerAttemptResultQueryHandler h, CancellationToken ct) =>
    await h.Handle(new GetReviewerAttemptResultQuery(new TestAttemptId(attemptId)), ct) is { } value ? Results.Ok(value) : Results.NotFound()).RequireAuthorization(Permissions.ResultsReview).RequireRateLimiting(RatePolicies.PrivilegedRead);
app.MapGet("/api/v1/operations/outbox", async (int? deadLetterLimit, OutboxMonitor monitor, CancellationToken ct) =>
    Results.Ok(await monitor.GetStatusAsync(deadLetterLimit ?? 20, ct))).RequireAuthorization(Permissions.OperationsRead).RequireRateLimiting(RatePolicies.Operations);
app.MapGet("/api/v1/operations/audit", async (string? actorId, int? statusCode, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, AuditTrail audit, CancellationToken ct) =>
    Results.Ok(await audit.GetAsync(actorId, statusCode, from, to, page ?? 1, pageSize ?? 20, ct))).RequireAuthorization(Permissions.OperationsRead).RequireRateLimiting(RatePolicies.Operations);

var attempts = app.MapGroup("/api/v1/attempts").RequireAuthorization();
attempts.MapPut("/{id:guid}/answers/{questionId:guid}", async (Guid id, Guid questionId, AnswerQuestionRequest r, AnswerQuestionCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new AnswerQuestionCommand(new TestAttemptId(id), new QuestionId(questionId), r.OptionIds.Select(x => new AnswerOptionId(x)).ToArray()), ct))).RequireRateLimiting(RatePolicies.StudentWrite);
attempts.MapDelete("/{id:guid}/answers/{questionId:guid}", async (Guid id, Guid questionId, ClearAnswerCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new ClearAnswerCommand(new TestAttemptId(id), new QuestionId(questionId)), ct))).RequireRateLimiting(RatePolicies.StudentWrite);
attempts.MapPost("/{id:guid}/submit", async (Guid id, HttpRequest request, SubmitAttemptRequest r, SubmitAttemptCommandHandler h, CancellationToken ct) =>
{
    var key = IdempotencyKeyResolver.Resolve(request, r.IdempotencyKey);
    return key.IsSuccess
        ? ToHttp(await h.Handle(new SubmitAttemptCommand(new TestAttemptId(id), key.Value), ct))
        : key.Error!;
}).RequireRateLimiting(RatePolicies.StudentWrite);
attempts.MapPost("/{id:guid}/timeout", async (Guid id, TimeoutAttemptCommandHandler h, CancellationToken ct) => ToHttp(await h.Handle(new TimeoutAttemptCommand(new TestAttemptId(id)), ct))).RequireAuthorization(Permissions.TestsAssign);
attempts.MapGet("/{id:guid}", async (Guid id, GetAttemptQueryHandler h, CancellationToken ct) => await h.Handle(new GetAttemptQuery(new TestAttemptId(id)), ct) is { } value ? Results.Ok(value) : Results.NotFound());
attempts.MapGet("/{id:guid}/result", async (Guid id, GetAttemptResultQueryHandler h, CancellationToken ct) => await h.Handle(new GetAttemptResultQuery(new TestAttemptId(id)), ct) is { } value ? Results.Ok(value) : Results.NotFound());

app.Run();

static RateLimitPartition<string> FixedWindowPartition(HttpContext context, RateLimitRule rule)
{
    var partitionKey = context.User.FindFirst("sub")?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";
    return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = rule.PermitLimit,
        Window = TimeSpan.FromSeconds(rule.WindowSeconds),
        QueueLimit = rule.QueueLimit,
        AutoReplenishment = true
    });
}

static IResult ToHttp<T>(TestApp.Core.Monads.Result<T, Error> result) where T : notnull => result.Match<IResult>(
    value => Results.Ok(value),
    error => Results.Problem(statusCode: error.Type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        _ => StatusCodes.Status500InternalServerError
    }, title: error.Code, detail: error.Message));

public static class Permissions
{
    public const string TestsWrite = "tests:write";
    public const string TestsPublish = "tests:publish";
    public const string TestsAssign = "tests:assign";
    public const string ResultsReview = "results:review";
    public const string OperationsRead = "operations:read";
}

public static class RatePolicies
{
    public const string StudentWrite = "student-write";
    public const string PrivilegedRead = "privileged-read";
    public const string Operations = "operations";
}

public static class CorsPolicies
{
    public const string Api = "api-cors";
}

public sealed record CreateTestRequest(string Title);
public sealed record RenameTestRequest(string Title);
public sealed record TestSettingsRequest(decimal PassingPercentage, int? TimeLimitMinutes);
public sealed record QuestionWriteRequest(string Text, QuestionType Type, decimal Points, int Order);
public sealed record QuestionUpdateRequest(string Text, QuestionType Type, decimal Points);
public sealed record AnswerOptionWriteRequest(string Text, bool IsCorrect, int Order);
public sealed record AnswerOptionUpdateRequest(string Text, bool IsCorrect);
public sealed record OrderRequest(int Order);
public sealed record PublishRequest(Guid IdempotencyKey);
public sealed record AssignRequest(Guid RevisionId, string? UserId, string? GroupId, DateTimeOffset AvailableFrom, DateTimeOffset? AvailableUntil, int? AttemptLimit, Guid IdempotencyKey);
public sealed record BulkAssignmentTargetRequest(string? UserId, string? GroupId);
public sealed record BulkAssignRequest(Guid RevisionId, IReadOnlyCollection<BulkAssignmentTargetRequest> Targets, DateTimeOffset AvailableFrom, DateTimeOffset? AvailableUntil, int? AttemptLimit, Guid IdempotencyKey);
public sealed record AssignmentWindowRequest(DateTimeOffset AvailableFrom, DateTimeOffset? AvailableUntil);
public sealed record AttemptLimitRequest(int? AttemptLimit);
public sealed record CancelAssignmentRequest(string? Reason);
public sealed record StartAttemptRequest(Guid IdempotencyKey);
public sealed record SubmitAttemptRequest(Guid IdempotencyKey);
public sealed record AnswerQuestionRequest(IReadOnlyCollection<Guid> OptionIds);

public partial class Program;