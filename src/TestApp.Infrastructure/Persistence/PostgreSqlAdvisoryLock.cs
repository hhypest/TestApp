using System.Buffers.Binary;
using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace TestApp.Infrastructure.Persistence;

internal static class PostgreSqlAdvisoryLock
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    public static async Task<IAsyncDisposable?> TryAcquireAsync(
        string connectionString,
        string resource,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            var key = CreateKey($"{connection.Database}|{resource}");
            var startedAt = Stopwatch.GetTimestamp();

            while (true)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_try_advisory_lock(@key);";
                command.Parameters.AddWithValue("key", key);
                var value = await command.ExecuteScalarAsync(ct);
                if (value is true)
                    return new Lease(connection, key);

                var elapsed = Stopwatch.GetElapsedTime(startedAt);
                if (timeout <= TimeSpan.Zero || elapsed >= timeout)
                {
                    await connection.DisposeAsync();
                    return null;
                }

                var remaining = timeout - elapsed;
                await Task.Delay(remaining < RetryDelay ? remaining : RetryDelay, ct);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static long CreateKey(string resource)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resource));
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    private sealed class Lease(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        private NpgsqlConnection? _connection = connection;

        public async ValueTask DisposeAsync()
        {
            var current = Interlocked.Exchange(ref _connection, null);
            if (current is null)
                return;

            try
            {
                if (current.State == ConnectionState.Open)
                {
                    await using var command = current.CreateCommand();
                    command.CommandText = "SELECT pg_advisory_unlock(@key);";
                    command.Parameters.AddWithValue("key", key);
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
