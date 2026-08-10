using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestApp.Application.Abstractions;
using TestApp.Infrastructure.Identity;
using TestApp.Infrastructure.Outbox;
using TestApp.Infrastructure.Persistence;

namespace TestApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<AppDbContext>(configureDb);
        services.AddHttpContextAccessor();
        services.AddScoped<ITestRepository, TestRepository>();
        services.AddScoped<IPublishedTestRevisionRepository, PublishedTestRevisionRepository>();
        services.AddScoped<ITestAssignmentRepository, TestAssignmentRepository>();
        services.AddScoped<ITestAttemptRepository, TestAttemptRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ICurrentActor, HttpCurrentActor>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOutboxPublisher, NullOutboxPublisher>();
        services.AddHostedService<OutboxProcessor>();
        return services;
    }
}
