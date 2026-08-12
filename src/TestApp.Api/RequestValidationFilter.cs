using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace TestApp.Api;

/// <summary>
/// Marker for public HTTP request DTOs that participate in transport validation.
/// Domain/application validation remains authoritative for business invariants.
/// </summary>
public interface IApiRequest
{
}

public sealed class RequestValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var parameters = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<MethodInfo>()?.GetParameters();

        for (var index = 0; index < context.Arguments.Count; index++)
        {
            var value = context.Arguments[index];
            var parameterName = parameters is not null && index < parameters.Length
                ? parameters[index].Name ?? $"arg{index}"
                : $"arg{index}";

            if (value is Enum enumValue)
            {
                ValidateEnum(enumValue, parameterName, errors);
                continue;
            }

            if (value is IApiRequest request)
                ValidateRequest(request, string.Empty, errors);
        }

        if (errors.Count == 0)
            return await next(context);

        var normalized = errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "request.validation",
            detail: "One or more request values are invalid.",
            instance: context.HttpContext.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["errors"] = normalized,
                ["traceId"] = context.HttpContext.TraceIdentifier
            });
    }

    private static void ValidateRequest(
        IApiRequest request,
        string path,
        Dictionary<string, List<string>> errors)
    {
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), validationResults, validateAllProperties: true);

        foreach (var result in validationResults)
        {
            var message = result.ErrorMessage ?? "The value is invalid.";
            var members = result.MemberNames.Any()
                ? result.MemberNames
                : new[] { string.Empty };
            foreach (var member in members)
                AddError(errors, Combine(path, ToJsonName(member)), message);
        }

        foreach (var property in request.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
                continue;

            var value = property.GetValue(request);
            var propertyPath = Combine(path, ToJsonName(property.Name));
            if (value is null)
                continue;

            if (value is Enum enumValue)
            {
                ValidateEnum(enumValue, propertyPath, errors);
                continue;
            }

            if (value is IApiRequest nestedRequest)
            {
                ValidateRequest(nestedRequest, propertyPath, errors);
                continue;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                var itemIndex = 0;
                foreach (var item in enumerable)
                {
                    if (item is IApiRequest nestedItem)
                        ValidateRequest(nestedItem, $"{propertyPath}[{itemIndex}]", errors);
                    else if (item is Enum itemEnum)
                        ValidateEnum(itemEnum, $"{propertyPath}[{itemIndex}]", errors);
                    itemIndex++;
                }
            }
        }
    }

    private static void ValidateEnum(
        Enum value,
        string path,
        Dictionary<string, List<string>> errors)
    {
        if (!Enum.IsDefined(value.GetType(), value))
            AddError(errors, path, "The value is not a supported enum member.");
    }

    private static void AddError(
        Dictionary<string, List<string>> errors,
        string path,
        string message)
    {
        var key = string.IsNullOrWhiteSpace(path) ? "request" : path;
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private static string Combine(string prefix, string member) =>
        string.IsNullOrEmpty(prefix)
            ? member
            : string.IsNullOrEmpty(member) ? prefix : $"{prefix}.{member}";

    private static string ToJsonName(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
            return name;

        return name.Length == 1
            ? char.ToLowerInvariant(name[0]).ToString()
            : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
