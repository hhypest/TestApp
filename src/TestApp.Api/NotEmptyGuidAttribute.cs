using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace TestApp.Api;

/// <summary>
/// Rejects the zero GUID in a request body, for a single value or for every element of a collection.
/// </summary>
/// <remarks>
/// The domain identifiers refuse to be constructed from <see cref="Guid.Empty"/> (ADR-029). Everything the
/// domain refuses to build has to be refused at the transport boundary first, otherwise a client can turn a
/// guard into a 500. This attribute is the body half of that boundary; route and query GUIDs are handled by
/// <see cref="RequestValidationFilter"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotEmptyGuidAttribute : ValidationAttribute
{
    public NotEmptyGuidAttribute()
        : base("The value must not be an empty GUID.")
    {
    }

    public override bool IsValid(object? value) => value switch
    {
        null => true,
        Guid single => single != Guid.Empty,
        IEnumerable elements => elements.Cast<object?>().All(static element => element is not Guid item || item != Guid.Empty),
        _ => true
    };
}
