namespace TestApp.Models.Errors;

public abstract record Error(string ErrorMessage, DateTimeOffset ErrorDateUtc);

public sealed record BadRequest(string Message) : Error(Message, DateTimeOffset.UtcNow);
public sealed record Unauthorized(string Message) : Error(Message, DateTimeOffset.UtcNow);
public sealed record Forbidden(string Message) : Error(Message, DateTimeOffset.UtcNow);
public sealed record NotFound(string Message) : Error(Message, DateTimeOffset.UtcNow);
public sealed record Conflict(string Message) : Error(Message, DateTimeOffset.UtcNow);
public sealed record ValidationError(string Message) : Error(Message, DateTimeOffset.UtcNow);