namespace TestApp.Domain.Common;

public enum DomainErrorType
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3
}

public sealed record DomainError(string Code, string Message, DomainErrorType Type)
{
    public static DomainError Validation(string code, string message) => new(code, message, DomainErrorType.Validation);
    public static DomainError NotFound(string code, string message) => new(code, message, DomainErrorType.NotFound);
    public static DomainError Conflict(string code, string message) => new(code, message, DomainErrorType.Conflict);
}
