using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TestApp.Api;
using TestApp.Api.Endpoints;
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
var apiLifecycleOptions = ApiLifecycleConfiguration.Load(builder.Configuration);

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
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

if (!migrateOnly)
{
    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        options.AddOperationTransformer<ApiContractOperationTransformer>();
        options.AddSchemaTransformer<ApiExampleSchemaTransformer>();
    });
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.Authority = keycloakOptions!.Authority;
        options.Audience = keycloakOptions.Audience;
        options.RequireHttpsMetadata = keycloakOptions.RequireHttpsMetadata;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "sub",
            RoleClaimType = "roles"
        };
    });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(Permissions.TestsWrite, policy => policy.RequireRole("test-author", "test-admin"));
        options.AddPolicy(Permissions.TestsPublish, policy => policy.RequireRole("test-author", "test-admin"));
        options.AddPolicy(Permissions.TestsAssign, policy => policy.RequireRole("test-admin"));
        options.AddPolicy(Permissions.ResultsReview, policy => policy.RequireRole("test-author", "test-admin"));
        options.AddPolicy(Permissions.OperationsRead, policy => policy.RequireRole("test-admin"));
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartitioning.FixedWindow(context, rateLimitingOptions.General));
        options.AddPolicy(RatePolicies.StudentWrite, context =>
            RateLimitPartitioning.FixedWindow(context, rateLimitingOptions.StudentWrite));
        options.AddPolicy(RatePolicies.PrivilegedRead, context =>
            RateLimitPartitioning.FixedWindow(context, rateLimitingOptions.PrivilegedRead));
        options.AddPolicy(RatePolicies.Operations, context =>
            RateLimitPartitioning.FixedWindow(context, rateLimitingOptions.Operations));
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

builder.Services.AddInfrastructure(options =>
    options.UseNpgsql(databaseOptions.ConnectionString));
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

builder.Services.AddApplicationHandlers();

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

app.UseMiddleware<LegacyApiCompatibilityMiddleware>(apiLifecycleOptions);

app.UseRouting();
if (corsOptions.Enabled)
    app.UseCors(CorsPolicies.Api);
app.UseMiddleware<RequestTelemetryMiddleware>();
// Audit must wrap exception mapping so it observes the final handled response status.
app.UseMiddleware<CorrelationAuditMiddleware>();
app.UseExceptionHandler();
app.UseAuthentication();
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

app.MapTestEndpoints();
app.MapAssignmentEndpoints();
app.MapAttemptEndpoints();
app.MapResultEndpoints();
app.MapOperationsEndpoints();

app.Run();

public partial class Program;
