using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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
        services.AddScoped<IAssignmentAdminQueries, AssignmentAdminQueries>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ICurrentActor, HttpCurrentActor>();
        services.AddScoped<OutboxMonitor>();
        services.AddScoped<AuditTrail>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(TimeProvider.System);
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("mariadb", tags: ["ready"]);
        services.AddSingleton<IStartupFilter, HealthEndpointStartupFilter>();
        AddObservability(services);
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

    public static IServiceCollection AddRabbitMqOutboxDelivery(
        this IServiceCollection services,
        Action<RabbitMqOutboxOptions> configureRabbitMq,
        Action<OutboxDeliveryOptions>? configureDelivery = null)
    {
        services.Configure(configureRabbitMq);
        if (configureDelivery is not null)
            services.Configure(configureDelivery);
        else
            services.Configure<OutboxDeliveryOptions>(_ => { });

        services.AddSingleton<RabbitMqOutboxPublisher>();
        services.AddSingleton<IOutboxPublisher>(sp => sp.GetRequiredService<RabbitMqOutboxPublisher>());
        services.AddHostedService<OutboxProcessor>();
        return services;
    }

    private static void AddObservability(IServiceCollection services)
    {
        var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "TestApp.Api";
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName));

        telemetry.WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health/live") &&
                        !context.Request.Path.StartsWithSegments("/health/ready");
                })
                .AddHttpClientInstrumentation();

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                tracing.AddOtlpExporter();
        });

        telemetry.WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation();

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                metrics.AddOtlpExporter();
        });
    }
}
