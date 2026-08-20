using System.Text.Json;
using Xunit;

namespace TestApp.IntegrationTests;

/// <summary>
/// Каталог, монтируемый в Keycloak локального стенда, обязан содержать только импортируемое им.
/// </summary>
/// <remarks>
/// <para>
/// `compose.yaml` монтирует <c>./deploy/keycloak</c> целиком в <c>/opt/keycloak/data/import</c>,
/// поэтому любой realm-файл, положенный в этот каталог, импортируется в локальный и CI Keycloak.
/// Файл, предназначенный другому окружению, роняет Keycloak на старте — а вместе с ним workflow
/// `postman` и `performance`, потому что они поднимают весь стек.
/// </para>
/// <para>
/// Именно это и произошло: realm контура, положенный рядом с dev-realm, содержал поле
/// <c>_comment</c>, которого нет в <c>RealmRepresentation</c>, и импорт упал с
/// <c>Unrecognized field "_comment"</c>. Ошибка проявилась только в CI и выглядела как отказ
/// двух несвязанных workflow.
/// </para>
/// <para>
/// Тест дешёвый и не требует ни Docker, ни базы: он смотрит на файлы. Артефакты контура живут
/// в <c>deploy/staging</c> — см. <c>deploy/staging/README.md</c>.
/// </para>
/// </remarks>
public sealed class KeycloakImportDirectoryTests
{
    private static readonly string[] AllowedRealmFiles = ["testapp-realm.json"];

    [Fact]
    public void The_local_import_directory_contains_only_the_development_realm()
    {
        var directory = Path.Combine(RepositoryRoot(), "deploy/keycloak");
        var actual = Directory.GetFiles(directory)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedRealmFiles.OrderBy(x => x, StringComparer.Ordinal).ToArray(), actual);
    }

    /// <summary>
    /// Локальный стенд не подставляет переменные окружения в realm, поэтому файл с
    /// плейсхолдерами импортируется буквально и даёт нерабочую конфигурацию.
    /// </summary>
    [Fact]
    public void The_development_realm_has_no_environment_placeholders()
    {
        var path = Path.Combine(RepositoryRoot(), "deploy/keycloak/testapp-realm.json");
        var content = File.ReadAllText(path);

        Assert.DoesNotContain("$(env:", content, StringComparison.Ordinal);
        Assert.DoesNotContain("${", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Keycloak отвергает весь файл при первом же неизвестном поле верхнего уровня, поэтому
    /// комментарии в realm-JSON недопустимы — пояснения живут в README рядом.
    /// </summary>
    [Theory]
    [InlineData("deploy/keycloak/testapp-realm.json")]
    [InlineData("deploy/staging/testapp-staging-realm.json")]
    public void Realm_files_declare_only_fields_keycloak_understands(string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath)));

        var unknown = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .Where(name => !KnownRealmFields.Contains(name))
            .ToArray();

        Assert.Empty(unknown);
    }

    /// <summary>
    /// Оба realm обязаны выдавать один и тот же набор claim'ов, потому что читает их один и тот
    /// же код: <c>RoleClaimType = "roles"</c> в <c>Program.cs</c>, claim <c>groups</c> в
    /// <c>KeycloakClaimsMapper</c> и обязательная audience <c>testapp-api</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Realm контура был заведён без единого protocol mapper. Keycloak при этом стартует, токен
    /// выдаёт, вход проходит — но в токене нет ни <c>roles</c>, ни <c>groups</c>, ни нужной
    /// <c>aud</c>. На контуре это выглядело бы как «всё поднялось, но любой запрос отвечает
    /// 401/403», причём одинаково для всех ролей, то есть максимально похоже на ошибку в самом
    /// приложении. Мапперы восстановлены; тест не даёт им снова разойтись.
    /// </para>
    /// <para>
    /// Сравнивается контракт, а не файлы целиком: у realm контура другой клиент
    /// (confidential, с секретом из окружения) и нет пользователей — это осознанные отличия.
    /// </para>
    /// </remarks>
    [Fact]
    public void Both_realms_issue_the_claims_the_application_reads()
    {
        foreach (var relativePath in new[] { "deploy/keycloak/testapp-realm.json", "deploy/staging/testapp-staging-realm.json" })
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath)));
            var root = document.RootElement;

            var client = root.GetProperty("clients")
                .EnumerateArray()
                .Single(element => element.GetProperty("clientId").GetString() == "testapp-api");

            var mappers = client.GetProperty("protocolMappers").EnumerateArray().ToArray();

            var claims = mappers
                .Select(mapper => mapper.GetProperty("config").TryGetProperty("claim.name", out var claim) ? claim.GetString() : null)
                .Where(claim => claim is not null)
                .ToArray();

            Assert.Contains("roles", claims);
            Assert.Contains("groups", claims);

            var audience = mappers.Single(mapper =>
                mapper.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper");
            Assert.Equal("testapp-api", audience.GetProperty("config").GetProperty("included.client.audience").GetString());

            // Группа, на которую выдаются групповые назначения, обязана существовать в обоих
            // realm: без неё назначение на группу не совпадёт ни с одним студентом.
            var groups = root.GetProperty("groups").EnumerateArray().Select(group => group.GetProperty("name").GetString());
            Assert.Contains("students", groups);

            var roles = root.GetProperty("roles").GetProperty("realm").EnumerateArray()
                .Select(role => role.GetProperty("name").GetString())
                .ToArray();
            Assert.Contains("test-author", roles);
            Assert.Contains("test-admin", roles);
        }
    }

    /// <summary>
    /// Подмножество <c>RealmRepresentation</c>, которое проект реально использует. Список
    /// намеренно узкий: расширять его следует осознанно, сверившись с моделью Keycloak,
    /// а не добавлять поле «раз оно уже написано в файле».
    /// </summary>
    private static readonly HashSet<string> KnownRealmFields = new(StringComparer.Ordinal)
    {
        "realm", "displayName", "enabled", "registrationAllowed", "loginWithEmailAllowed",
        "sslRequired", "bruteForceProtected", "roles", "groups", "clients", "users",
        "accessTokenLifespan", "ssoSessionIdleTimeout", "ssoSessionMaxLifespan"
    };

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TestApp.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new Xunit.Sdk.XunitException("Не найден корень репозитория (TestApp.slnx).");
    }
}
