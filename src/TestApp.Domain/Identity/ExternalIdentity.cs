using System.Text.Json.Serialization;

namespace TestApp.Domain.Identity;

public static class ExternalIdentityLimits
{
    public const int MaxIdentifierLength = 256;

    /// <summary>
    /// Guard shared by <see cref="ExternalUserId"/> and <see cref="ExternalGroupId"/>. It runs in the
    /// constructor so that the validating factories and the EF Core value converters cannot diverge.
    /// </summary>
    internal static string Ensure(string value, string identifierName, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaxIdentifierLength)
            throw new ArgumentException($"{identifierName} cannot exceed {MaxIdentifierLength} characters.", parameterName);

        return value;
    }
}

/// <summary>
/// Stable identifier of a user owned by the external identity provider (Keycloak).
/// TestApp never generates this value.
/// </summary>
public readonly record struct ExternalUserId
{
    [JsonConstructor]
    public ExternalUserId(string value) =>
        Value = ExternalIdentityLimits.Ensure(value, "External user identifier", nameof(value));

    public string Value { get; }

    /// <summary>Reads the identifier out of the Keycloak <c>sub</c> claim.</summary>
    public static ExternalUserId FromSubject(string subject) => new(subject);

    public override string ToString() => Value;
}

/// <summary>
/// Stable identifier of a group owned by the external identity provider (Keycloak).
/// TestApp never generates this value.
/// </summary>
public readonly record struct ExternalGroupId
{
    [JsonConstructor]
    public ExternalGroupId(string value) =>
        Value = ExternalIdentityLimits.Ensure(value, "External group identifier", nameof(value));

    public string Value { get; }

    /// <summary>Reads the identifier out of the group representation owned by the identity provider.</summary>
    public static ExternalGroupId FromExternalId(string value) => new(value);

    public override string ToString() => Value;
}
