using Npgsql;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class PostgreSqlVersionTests
{
    [Fact]
    public async Task Integration_database_is_PostgreSQL_18_or_newer()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable("TESTAPP_POSTGRES_ADMIN")
            ?? "Host=127.0.0.1;Port=5432;Username=testapp;Password=testapp;Database=postgres;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW server_version_num;";
        var rawVersion = Assert.IsType<string>(await command.ExecuteScalarAsync(ct));

        Assert.True(
            int.TryParse(rawVersion, out var versionNumber),
            $"Could not parse PostgreSQL server_version_num from '{rawVersion}'.");
        Assert.True(
            versionNumber >= 180000,
            $"Expected PostgreSQL 18 or newer, but server_version_num was '{rawVersion}'.");
    }
}
