namespace TestApp.Api;

public static partial class RuntimeConfiguration
{
    private const string DevelopmentDatabase =
        "Host=localhost;Port=5432;Database=testapp;Username=testapp;Password=testapp;";

    public static DatabaseRuntimeOptions LoadDatabase(IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException(
                    "ConnectionStrings:Database is required outside Development. Production database credentials must be supplied explicitly.");
            connectionString = DevelopmentDatabase;
        }

        var applyMigrations = configuration.GetValue<bool?>("Database:ApplyMigrationsOnStartup")
            ?? environment.IsDevelopment();
        if (!environment.IsDevelopment() && applyMigrations)
            throw new InvalidOperationException(
                "Database:ApplyMigrationsOnStartup=true is not allowed outside Development. Use the --migrate release/init job instead.");

        return new DatabaseRuntimeOptions(connectionString, applyMigrations);
    }

    public static KeycloakRuntimeOptions LoadKeycloak(IConfiguration configuration, IHostEnvironment environment)
    {
        var authority = Require(configuration["Keycloak:Authority"], "Keycloak:Authority");
        var audience = Require(configuration["Keycloak:Audience"], "Keycloak:Audience");
        var requireHttpsMetadata = configuration.GetValue<bool?>("Keycloak:RequireHttpsMetadata")
            ?? !environment.IsDevelopment();

        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
            (authorityUri.Scheme != Uri.UriSchemeHttp && authorityUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Keycloak:Authority must be an absolute HTTP(S) URI.");
        if (requireHttpsMetadata && authorityUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Keycloak:Authority must use HTTPS when Keycloak:RequireHttpsMetadata=true.");

        return new KeycloakRuntimeOptions(authority.TrimEnd('/'), audience, requireHttpsMetadata);
    }

    private static string Require(string? value, string key) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"{key} is required for the HTTP API host.");
}
