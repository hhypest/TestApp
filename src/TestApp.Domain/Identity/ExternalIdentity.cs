namespace TestApp.Domain.Identity;

/// <summary>
/// Stable identifier of a user owned by the external identity provider (Keycloak).
/// TestApp never generates this value.
/// </summary>
public readonly record struct ExternalUserId(string Value)
{
    public static ExternalUserId FromSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return new(subject);
    }

    public override string ToString() => Value;
}

/// <summary>
/// Stable identifier of a group owned by the external identity provider (Keycloak).
/// TestApp never generates this value.
/// </summary>
public readonly record struct ExternalGroupId(string Value)
{
    public static ExternalGroupId FromExternalId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new(value);
    }

    public override string ToString() => Value;
}
