using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TestApp.Domain.Identity;
using TestApp.Infrastructure.Observability;
using TestApp.Infrastructure.Persistence;

namespace TestApp.Infrastructure.Outbox;

public static class OutboxDeadLetterActionLimits
{
    public const int ReasonMaxLength = 1000;
    public const int CorrelationIdMaxLength = 128;
}

public enum OutboxDeadLetterActionType
{
    Requeued = 1,
    Discarded = 2
}

public sealed class OutboxDeadLetterAction
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public OutboxDeadLetterActionType Action { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;

    private OutboxDeadLetterAction() { }

    public static OutboxDeadLetterAction Create(
        Guid eventId,
        OutboxDeadLetterActionType action,
        ExternalUserId actor,
        string reason,
        DateTimeOffset occurredAt,
        string correlationId)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event id cannot be empty.", nameof(eventId));
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (string.IsNullOrWhiteSpace(actor.Value)) throw new ArgumentException("Actor is required.", nameof(actor));
        if (actor.Value.Length > ExternalIdentityLimits.MaxIdentifierLength) throw new ArgumentException("Actor identifier is too long.", nameof(actor));

        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length is < 1 or > OutboxDeadLetterActionLimits.ReasonMaxLength)
            throw new ArgumentException($"Reason must contain 1..{OutboxDeadLetterActionLimits.ReasonMaxLength} characters.", nameof(reason));

        var normalizedCorrelation = correlationId?.Trim() ?? string.Empty;
        if (normalizedCorrelation.Length is < 1 or > OutboxDeadLetterActionLimits.CorrelationIdMaxLength)
            throw new ArgumentException($"Correlation id must contain 1..{OutboxDeadLetterActionLimits.CorrelationIdMaxLength} characters.", nameof(correlationId));

        return new OutboxDeadLetterAction
        {
            Id = Guid.CreateVersion7(),
            EventId = eventId,
            Action = action,
            ActorId = actor.Value,
            Reason = normalizedReason,
            OccurredAt = occurredAt.ToUniversalTime(),
            CorrelationId = normalizedCorrelation
        };
    }
}

public sealed class OutboxDeadLetterActionConfiguration : IEntityTypeConfiguration<OutboxDeadLetterAction>
{
    public void Configure(EntityTypeBuilder<OutboxDeadLetterAction> b)
    {
        b.ToTable("outbox_dead_letter_actions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasConversion<int>().IsRequired();
        b.Property(x => x.ActorId).HasMaxLength(ExternalIdentityLimits.MaxIdentifierLength).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(OutboxDeadLetterActionLimits.ReasonMaxLength).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(OutboxDeadLetterActionLimits.CorrelationIdMaxLength).IsRequired();
        b.HasIndex(x => new { x.EventId, x.OccurredAt });
        b.HasIndex(x => x.OccurredAt);
    }
}

public sealed record OutboxDeadLetterDetail(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    int AttemptCount,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? DeadLetteredAt,
    DateTimeOffset? DiscardedAt,
    string? Error);

public enum OutboxDeadLetterCommandStatus
{
    Success = 1,
    NotFound = 2,
    Conflict = 3,
    Busy = 4
}

public sealed record OutboxDeadLetterCommandResult(
    OutboxDeadLetterCommandStatus Status,
    OutboxDeadLetterDetail? Detail,
    string Code,
    string Message);

public sealed class OutboxDeadLetterManager(
    AppDbContext db,
    TimeProvider time,
    OperationalMetrics metrics)
{
    private const int LockTimeoutSeconds = 5;

    public Task<OutboxDeadLetterDetail?> GetAsync(Guid eventId, CancellationToken ct = default) =>
        db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.Id == eventId && x.DeadLetteredAt != null)
            .Select(x => new OutboxDeadLetterDetail(
                x.Id,
                x.Type,
                x.OccurredAt,
                x.AttemptCount,
                x.LastAttemptAt,
                x.DeadLetteredAt,
                x.DiscardedAt,
                x.Error))
            .SingleOrDefaultAsync(ct);

    public Task<OutboxDeadLetterCommandResult> RequeueAsync(
        Guid eventId,
        ExternalUserId actor,
        string reason,
        string correlationId,
        CancellationToken ct = default) =>
        ExecuteAsync(eventId, actor, reason, correlationId, OutboxDeadLetterActionType.Requeued, ct);

    public Task<OutboxDeadLetterCommandResult> DiscardAsync(
        Guid eventId,
        ExternalUserId actor,
        string reason,
        string correlationId,
        CancellationToken ct = default) =>
        ExecuteAsync(eventId, actor, reason, correlationId, OutboxDeadLetterActionType.Discarded, ct);

    private async Task<OutboxDeadLetterCommandResult> ExecuteAsync(
        Guid eventId,
        ExternalUserId actor,
        string reason,
        string correlationId,
        OutboxDeadLetterActionType action,
        CancellationToken ct)
    {
        await using var lease = await OutboxAdvisoryLock.TryAcquireAsync(db, eventId, LockTimeoutSeconds, ct);
        if (lease is null)
        {
            return new OutboxDeadLetterCommandResult(
                OutboxDeadLetterCommandStatus.Busy,
                null,
                "outbox.dead_letter.busy",
                "The Outbox message is currently being processed. Retry the management operation.");
        }

        db.ChangeTracker.Clear();
        var message = await db.OutboxMessages.SingleOrDefaultAsync(x => x.Id == eventId, ct);
        if (message is null || message.DeadLetteredAt is null)
        {
            return new OutboxDeadLetterCommandResult(
                OutboxDeadLetterCommandStatus.NotFound,
                null,
                "outbox.dead_letter.not_found",
                "The dead-lettered Outbox message was not found.");
        }

        if (message.ProcessedAt is not null || message.DiscardedAt is not null)
        {
            return new OutboxDeadLetterCommandResult(
                OutboxDeadLetterCommandStatus.Conflict,
                ToDetail(message),
                "outbox.dead_letter.state",
                "The Outbox message is no longer an active dead letter.");
        }

        var now = time.GetUtcNow();
        if (action == OutboxDeadLetterActionType.Requeued)
            message.RequeueDeadLetter(now);
        else
            message.DiscardDeadLetter(now);

        db.OutboxDeadLetterActions.Add(OutboxDeadLetterAction.Create(
            eventId,
            action,
            actor,
            reason,
            now,
            correlationId));
        await db.SaveChangesAsync(ct);
        metrics.RecordDeadLetterAction(action == OutboxDeadLetterActionType.Requeued ? "requeue" : "discard");

        return new OutboxDeadLetterCommandResult(
            OutboxDeadLetterCommandStatus.Success,
            ToDetail(message),
            action == OutboxDeadLetterActionType.Requeued ? "outbox.dead_letter.requeued" : "outbox.dead_letter.discarded",
            action == OutboxDeadLetterActionType.Requeued ? "The dead letter was requeued." : "The dead letter was discarded.");
    }

    private static OutboxDeadLetterDetail ToDetail(OutboxMessage message) => new(
        message.Id,
        message.Type,
        message.OccurredAt,
        message.AttemptCount,
        message.LastAttemptAt,
        message.DeadLetteredAt,
        message.DiscardedAt,
        message.Error);
}
