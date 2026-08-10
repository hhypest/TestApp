using TestApp.Domain.Common;

namespace TestApp.Application.Common;

public static class DomainErrorMappingExtensions
{
    public static Error ToApplicationError(this DomainError error) => error.Type switch
    {
        DomainErrorType.Validation => Error.Validation(error.Code, error.Message),
        DomainErrorType.NotFound => Error.NotFound(error.Code, error.Message),
        DomainErrorType.Conflict => Error.Conflict(error.Code, error.Message),
        _ => Error.Conflict(error.Code, error.Message)
    };
}
