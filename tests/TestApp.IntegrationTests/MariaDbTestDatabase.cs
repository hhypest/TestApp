using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using TestApp.Infrastructure.Persistence;

namespace TestApp.IntegrationTests;

internal sealed class MariaDbTestDatabase : IAsyncDisposable
{
    private readonly string _rootConnectionString;

    public string DatabaseName { get; }
    public string ConnectionString { get; }

    private MariaDbTestDatabase(string rootConnectionString, string databaseName, string connectionString)
    {
        _rootConnectionString = rootConnectionString;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    public static async Task<MariaDbTestDatabase> CreateAsync(CancellationToken ct)
    {
        var root = Environment.GetEnvironmentVariable("TESTAPP_MARIADB_ROOT")
            ?? "Server=127.0.0.1;Port=3306;User=root;Password=root;";
        var databaseName = $"testapp_{Guid.NewGuid():N}";

        await using (var connection = new MySqlConnection(root))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await command.ExecuteNonQueryAsync(ct);
        }

        var builder = new MySqlConnectionStringBuilder(root) { Database = databaseName };
        return new MariaDbTestDatabase(root, databaseName, builder.ConnectionString);
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(ConnectionString, new MariaDbServerVersion(new Version(11, 4, 0)))
            .Options;
        return new AppDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new MySqlConnection(_rootConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{DatabaseName}`;";
        await command.ExecuteNonQueryAsync();
    }
}
