namespace TestApp.Domain.Identity;

public static class ExternalIdentityLimits
{
    public const int MaxIdentifierLength = 256;
}

/// <summary>
/// Stable identifier of a user owned by the external identity provider (Keycloak).
/// TestApp never generates this value.
/// </summary>
public readonly record struct ExternalUserId(string Value)
{
    public static ExternalUserId FromSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (subject.Length > ExternalIdentityLimits.MaxIdentifierLength)
            throw new ArgumentException($"External user identifier cannot exceed {ExternalIdentityLimits.MaxIdentifierLength} characters.", nameof(subject));
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
        if (value.Length > ExternalIdentityLimits.MaxIdentifierLength)
            throw new ArgumentException($"External group identifier cannot exceed {ExternalIdentityLimits.MaxIdentifierLength} characters.", nameof(value));
        return new(value);
    }

    public override string ToString() => Value;
}
