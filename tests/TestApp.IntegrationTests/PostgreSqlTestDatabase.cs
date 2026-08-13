using Microsoft.EntityFrameworkCore;
using Npgsql;
using TestApp.Infrastructure.Persistence;

namespace TestApp.IntegrationTests;

internal sealed class PostgreSqlTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;

    public string DatabaseName { get; }
    public string ConnectionString { get; }

    private PostgreSqlTestDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    public static async Task<PostgreSqlTestDatabase> CreateAsync(CancellationToken ct)
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("TESTAPP_POSTGRES_ADMIN")
            ?? "Host=127.0.0.1;Port=5432;Username=testapp;Password=testapp;Database=postgres;";
        var databaseName = $"testapp_{Guid.NewGuid():N}";

        var adminBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using (var connection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)};";
            await command.ExecuteNonQueryAsync(ct);
        }

        var databaseBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        return new PostgreSqlTestDatabase(
            adminBuilder.ConnectionString,
            databaseName,
            databaseBuilder.ConnectionString);
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();

        await using (var terminate = connection.CreateCommand())
        {
            terminate.CommandText = """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @database AND pid <> pg_backend_pid();
                """;
            terminate.Parameters.AddWithValue("database", DatabaseName);
            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(DatabaseName)};";
        await drop.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
