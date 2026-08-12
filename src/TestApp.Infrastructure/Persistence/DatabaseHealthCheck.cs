using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TestApp.Infrastructure.Persistence;

public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("MariaDB is reachable.")
                : HealthCheckResult.Unhealthy("MariaDB is not reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("MariaDB readiness check failed.", exception);
        }
    }
}
