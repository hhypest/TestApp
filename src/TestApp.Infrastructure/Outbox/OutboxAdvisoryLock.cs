using Microsoft.EntityFrameworkCore;
using TestApp.Infrastructure.Persistence;

namespace TestApp.Infrastructure.Outbox;

internal static class OutboxAdvisoryLock
{
    public static async Task<IAsyncDisposable?> TryAcquireAsync(
        AppDbContext db,
        Guid eventId,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Database connection string is not configured.");
        return await PostgreSqlAdvisoryLock.TryAcquireAsync(
            connectionString,
            $"outbox:{eventId:N}",
            TimeSpan.FromSeconds(timeoutSeconds),
            ct);
    }
}
