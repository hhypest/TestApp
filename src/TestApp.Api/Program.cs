using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestApp.Application.Assignments;
using TestApp.Application.Attempts;
using TestApp.Application.Common;
using TestApp.Application.Tests;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Infrastructure;
using TestApp.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.Authority = builder.Configuration["Keycloak:Authority"];
    o.Audience = builder.Configuration["Keycloak:Audience"];
    o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
});
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("test-admin", p => p.RequireRole("test-admin"));
    o.AddPolicy("test-author", p => p.RequireRole("test-author", "test-admin"));
});
builder.Services.AddInfrastructure(o => o.UseSqlite(builder.Configuration.GetConnectionString("Database") ?? "Data Source=testapp.db"));
builder.Services.AddScoped<PublishTestCommandHandler>();
builder.Services.AddScoped<AssignTestCommandHandler>();
builder.Services.AddScoped<StartAttemptCommandHandler>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

var tests = app.MapGroup("/api/tests").RequireAuthorization();
tests.MapPost("/{id:guid}/publish", async (Guid id, PublishTestCommandHandler handler, CancellationToken ct) =>
    ToHttp(await handler.Handle(new PublishTestCommand(new TestId(id)), ct))).RequireAuthorization("test-author");

tests.MapPost("/assignments", async (AssignRequest request, AssignTestCommandHandler handler, CancellationToken ct) =>
{
    ExternalUserId? user = string.IsNullOrWhiteSpace(request.UserId) ? null : ExternalUserId.FromSubject(request.UserId);
    ExternalGroupId? group = string.IsNullOrWhiteSpace(request.GroupId) ? null : ExternalGroupId.FromExternalId(request.GroupId);
    return ToHttp(await handler.Handle(new AssignTestCommand(new PublishedTestRevisionId(request.RevisionId), user, group, request.AvailableFrom, request.AvailableUntil, request.AttemptLimit), ct));
}).RequireAuthorization("test-admin");

tests.MapPost("/assignments/{id:guid}/attempts", async (Guid id, StartAttemptCommandHandler handler, CancellationToken ct) =>
    ToHttp(await handler.Handle(new StartAttemptCommand(new TestApp.Domain.Assignments.TestAssignmentId(id)), ct)));

app.Run();

static IResult ToHttp<T>(TestApp.Core.Monads.Result<T, Error> result) where T : notnull => result.Match<IResult>(
    value => Results.Ok(value),
    error => Results.Problem(statusCode: error.Type switch { ErrorType.Validation => 400, ErrorType.NotFound => 404, ErrorType.Conflict => 409, ErrorType.Forbidden => 403, _ => 500 }, title: error.Code, detail: error.Message));

public sealed record AssignRequest(Guid RevisionId, string? UserId, string? GroupId, DateTimeOffset AvailableFrom, DateTimeOffset? AvailableUntil, int? AttemptLimit);
public partial class Program;
