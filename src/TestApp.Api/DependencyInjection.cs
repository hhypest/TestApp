using TestApp.Application.Assignments;
using TestApp.Application.Attempts;
using TestApp.Application.Queries;
using TestApp.Application.Tests;

namespace TestApp.Api;

internal static class DependencyInjection
{
    public static IServiceCollection AddApplicationHandlers(this IServiceCollection services)
    {
        services.AddScoped<CreateTestCommandHandler>();
        services.AddScoped<RenameTestCommandHandler>();
        services.AddScoped<ChangeTestSettingsCommandHandler>();
        services.AddScoped<AddQuestionCommandHandler>();
        services.AddScoped<UpdateQuestionCommandHandler>();
        services.AddScoped<RemoveQuestionCommandHandler>();
        services.AddScoped<ReorderQuestionCommandHandler>();
        services.AddScoped<AddAnswerOptionCommandHandler>();
        services.AddScoped<UpdateAnswerOptionCommandHandler>();
        services.AddScoped<RemoveAnswerOptionCommandHandler>();
        services.AddScoped<ReorderAnswerOptionCommandHandler>();
        services.AddScoped<ArchiveTestCommandHandler>();
        services.AddScoped<PublishTestCommandHandler>();
        services.AddScoped<AssignTestCommandHandler>();
        services.AddScoped<BulkAssignTestsCommandHandler>();
        services.AddScoped<CancelAssignmentCommandHandler>();
        services.AddScoped<ChangeAssignmentWindowCommandHandler>();
        services.AddScoped<ChangeAssignmentAttemptLimitCommandHandler>();
        services.AddScoped<StartAttemptCommandHandler>();
        services.AddScoped<AnswerQuestionCommandHandler>();
        services.AddScoped<ClearAnswerCommandHandler>();
        services.AddScoped<SubmitAttemptCommandHandler>();
        services.AddScoped<TimeoutAttemptCommandHandler>();
        services.AddScoped<GetTestsQueryHandler>();
        services.AddScoped<GetTestRevisionsQueryHandler>();
        services.AddScoped<GetTestEditorViewQueryHandler>();
        services.AddScoped<GetAdminAssignmentsQueryHandler>();
        services.AddScoped<GetAdminAssignmentQueryHandler>();
        services.AddScoped<GetAdminAssignmentAttemptsQueryHandler>();
        services.AddScoped<GetMyAssignmentsQueryHandler>();
        services.AddScoped<GetMyAttemptsQueryHandler>();
        services.AddScoped<GetAttemptQueryHandler>();
        services.AddScoped<GetAttemptResultQueryHandler>();
        services.AddScoped<GetReviewerResultsQueryHandler>();
        services.AddScoped<GetReviewerAttemptResultQueryHandler>();

        return services;
    }
}
