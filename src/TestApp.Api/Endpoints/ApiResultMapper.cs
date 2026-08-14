using TestApp.Application.Common;
using TestApp.Core.Monads;

namespace TestApp.Api.Endpoints;

internal static class ApiResultMapper
{
    public static IResult ToHttp<T>(Result<T, Error> result) where T : notnull => result.Match<IResult>(
        value => Results.Ok(value),
        error => Results.Problem(statusCode: error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
            _ => StatusCodes.Status500InternalServerError
        }, title: error.Code, detail: error.Message));
}
