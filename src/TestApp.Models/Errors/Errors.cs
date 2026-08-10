namespace TestApp.Models.Errors;

public abstract record Error(string Code, string Message);

public sealed record NotFound(string Code, string Message) : Error(Code, Message);
public sealed record Conflict(string Code, string Message) : Error(Code, Message);
public sealed record ValidationError(string Code, string Message) : Error(Code, Message);
public sealed record Unauthorized(string Code, string Message) : Error(Code, Message);
public sealed record Forbidden(string Code, string Message) : Error(Code, Message);
