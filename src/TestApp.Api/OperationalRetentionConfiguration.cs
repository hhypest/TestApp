namespace TestApp.Api;

public sealed record OperationalRetentionRuntimeOptions(
    bool Enabled,
    int AuditRetentionDays,
    int IdempotencyRetentionDays,
    int ProcessedOutboxRetentionDays,
    int BatchSize,
    int MaxBatchesPerRun,
    int PollIntervalMinutes);

public static class OperationalRetentionConfiguration
{
    public static OperationalRetentionRuntimeOptions Load(IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool?>("OperationalRetention:Enabled") ?? true;
        var auditDays = configuration.GetValue<int?>("OperationalRetention:AuditRetentionDays") ?? 90;
        var idempotencyDays = configuration.GetValue<int?>("OperationalRetention:IdempotencyRetentionDays") ?? 14;
        var processedOutboxDays = configuration.GetValue<int?>("OperationalRetention:ProcessedOutboxRetentionDays") ?? 14;
        var batchSize = configuration.GetValue<int?>("OperationalRetention:BatchSize") ?? 500;
        var maxBatches = configuration.GetValue<int?>("OperationalRetention:MaxBatchesPerRun") ?? 20;
        var pollMinutes = configuration.GetValue<int?>("OperationalRetention:PollIntervalMinutes") ?? 360;

        if (auditDays < 1) throw new InvalidOperationException("OperationalRetention:AuditRetentionDays must be greater than zero.");
        if (idempotencyDays < 1) throw new InvalidOperationException("OperationalRetention:IdempotencyRetentionDays must be greater than zero.");
        if (processedOutboxDays < 1) throw new InvalidOperationException("OperationalRetention:ProcessedOutboxRetentionDays must be greater than zero.");
        if (batchSize is < 1 or > 5000) throw new InvalidOperationException("OperationalRetention:BatchSize must be between 1 and 5000.");
        if (maxBatches is < 1 or > 1000) throw new InvalidOperationException("OperationalRetention:MaxBatchesPerRun must be between 1 and 1000.");
        if (pollMinutes < 1) throw new InvalidOperationException("OperationalRetention:PollIntervalMinutes must be greater than zero.");

        return new OperationalRetentionRuntimeOptions(
            enabled,
            auditDays,
            idempotencyDays,
            processedOutboxDays,
            batchSize,
            maxBatches,
            pollMinutes);
    }
}
