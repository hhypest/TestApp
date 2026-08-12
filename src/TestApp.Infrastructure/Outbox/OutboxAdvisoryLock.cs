using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
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
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var material = $"{connection.Database}|{eventId:N}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        var lockName = $"testapp:outbox:{hash[..48]}";

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT GET_LOCK(@name, @timeoutSeconds);";
            command.Parameters.AddWithValue("@name", lockName);
            command.Parameters.AddWithValue("@timeoutSeconds", timeoutSeconds);
            var result = await command.ExecuteScalarAsync(ct);

            if (result is null || result is DBNull || Convert.ToInt32(result) != 1)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new Lease(connection, lockName);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(MySqlConnection connection, string lockName) : IAsyncDisposable
    {
        private MySqlConnection? _connection = connection;

        public async ValueTask DisposeAsync()
        {
            var current = Interlocked.Exchange(ref _connection, null);
            if (current is null)
                return;

            try
            {
                if (current.State == System.Data.ConnectionState.Open)
                {
                    await using var command = current.CreateCommand();
                    command.CommandText = "SELECT RELEASE_LOCK(@name);";
                    command.Parameters.AddWithValue("@name", lockName);
                    await command.ExecuteScalarAsync();
                }
            }
            finally
            {
                await current.DisposeAsync();
            }
        }
    }
}
