using MySqlConnector;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class MariaDbVersionTests
{
    [Fact]
    public async Task Integration_database_is_MariaDB_12_3_or_newer()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable("TESTAPP_MARIADB_ROOT")
            ?? "Server=127.0.0.1;Port=3306;User=root;Password=root;";

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT VERSION();";
        var rawVersion = Assert.IsType<string>(await command.ExecuteScalarAsync(ct));

        var numericPart = rawVersion.Split('-', 2)[0];
        Assert.True(
            Version.TryParse(numericPart, out var version),
            $"Could not parse MariaDB version from '{rawVersion}'.");
        Assert.True(
            version >= new Version(12, 3, 0),
            $"Expected MariaDB 12.3 or newer, but server reported '{rawVersion}'.");
    }
}
