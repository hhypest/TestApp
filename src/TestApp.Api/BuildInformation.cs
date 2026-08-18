using System.Reflection;

namespace TestApp.Api;

/// <summary>
/// Что именно развёрнуто: версия сборки и digest образа, из которого запущен процесс.
/// </summary>
/// <param name="Version">
/// Информационная версия API-сборки — то, что собрал MSBuild из `VersionPrefix` в
/// `Directory.Build.props` и `VersionSuffix`, переданного релизным пайплайном (`1.0.0`, `1.0.0-rc.1`).
/// </param>
/// <param name="ImageDigest">
/// Immutable digest образа GHCR (`sha256:…`), проставляемый развёртыванием через переменную
/// окружения <c>TESTAPP_IMAGE_DIGEST</c>. <c>null</c>, если процесс запущен не из образа
/// (локальная сборка) либо развёртывание переменную не задало.
/// </param>
public sealed record BuildInformation(string Version, string? ImageDigest)
{
    public const string ImageDigestVariable = "TESTAPP_IMAGE_DIGEST";

    /// <summary>
    /// Версия читается из атрибута сборки, а не из константы: константу легко забыть поднять,
    /// и тогда развёрнутый образ будет уверенно называть неверную версию — это хуже, чем не
    /// сообщать её вовсе.
    /// </summary>
    public static BuildInformation Current { get; } = Create(typeof(BuildInformation).Assembly);

    public static BuildInformation Create(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        // MSBuild добавляет к информационной версии метаданные сборки после '+' (например хеш
        // коммита от SourceLink). Для операционного ответа нужна версия, а не build metadata.
        var version = informational is { Length: > 0 }
            ? informational.Split('+')[0]
            : assembly.GetName().Version?.ToString() ?? "unknown";

        var digest = Environment.GetEnvironmentVariable(ImageDigestVariable);

        return new BuildInformation(version, string.IsNullOrWhiteSpace(digest) ? null : digest.Trim());
    }
}
