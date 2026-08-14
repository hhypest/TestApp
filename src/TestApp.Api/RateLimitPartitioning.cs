using System.Threading.RateLimiting;

namespace TestApp.Api;

internal static class RateLimitPartitioning
{
    public static RateLimitPartition<string> FixedWindow(HttpContext context, RateLimitRule rule)
    {
        var partitionKey = context.User.FindFirst("sub")?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rule.PermitLimit,
            Window = TimeSpan.FromSeconds(rule.WindowSeconds),
            QueueLimit = rule.QueueLimit,
            AutoReplenishment = true
        });
    }
}
