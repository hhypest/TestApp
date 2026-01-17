namespace TestApp.Core.Data;

public abstract record Error(int ErrorCode, string ErrorMessage, DateTimeOffset ErrorDateUtc);

public sealed record BadRequest(string Message) : Error(400, Message, DateTimeOffset.UtcNow);
public sealed record Unauthorized(string Message) : Error(401, Message, DateTimeOffset.UtcNow);
public sealed record Forbidden(string Message) : Error(403, Message, DateTimeOffset.UtcNow);
public sealed record NotFound(string Message) : Error(404, Message, DateTimeOffset.UtcNow);
public sealed record Conflict(string Message) : Error(409, Message, DateTimeOffset.UtcNow);
public sealed record ValidationError(string Message) : Error(999, Message, DateTimeOffset.UtcNow);