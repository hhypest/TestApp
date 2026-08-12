using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestApp.Application.Abstractions;
using TestApp.Application.Queries;
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
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IReadModelQueries, ReadModelQueries>();
        services.AddScoped<ITestCatalogQueries, TestCatalogQueries>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ICurrentActor, HttpCurrentActor>();
        services.AddScoped<OutboxMonitor>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(TimeProvider.System);
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("mariadb", tags: ["ready"]);
        services.AddSingleton<IStartupFilter, HealthEndpointStartupFilter>();
        return services;
    }

    public static IServiceCollection AddOutboxDelivery<TPublisher>(
        this IServiceCollection services,
        Action<OutboxDeliveryOptions>? configure = null)
        where TPublisher : class, IOutboxPublisher
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<OutboxDeliveryOptions>(_ => { });

        services.AddScoped<IOutboxPublisher, TPublisher>();
        services.AddHostedService<OutboxProcessor>();
        return services;
    }
}
