using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TestApp.Application.Abstractions;
using TestApp.Domain.Identity;

namespace TestApp.Infrastructure.Persistence;

public sealed class IdempotencyRecord
{
    public Guid Id { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string ActorId { get; private set; } = string.Empty;
    public Guid RequestId { get; private set; }
    public string ResultType { get; private set; } = string.Empty;
    public string ResultJson { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private IdempotencyRecord() { }

    public static IdempotencyRecord Create<T>(string operation, ExternalUserId actorId, Guid requestId, T result, DateTimeOffset createdAt) where T : struct
    {
        if (string.IsNullOrWhiteSpace(operation))
            throw new ArgumentException("Operation is required.", nameof(operation));
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request id cannot be empty.", nameof(requestId));

        return new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(),
            Operation = operation,
            ActorId = actorId.Value,
            RequestId = requestId,
            ResultType = typeof(T).FullName ?? typeof(T).Name,
            ResultJson = JsonSerializer.Serialize(result, JsonSerializerOptions.Default),
            CreatedAt = createdAt
        };
    }

    public T ToResult<T>() where T : struct =>
        JsonSerializer.Deserialize<T>(ResultJson, JsonSerializerOptions.Default);
}

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_records");
        b.HasKey(x => x.Id);
        b.Property(x => x.Operation).HasMaxLength(200).IsRequired();
        b.Property(x => x.ActorId).HasMaxLength(256).IsRequired();
        b.Property(x => x.ResultType).HasMaxLength(512).IsRequired();
        b.Property(x => x.ResultJson).IsRequired();
        b.HasIndex(x => new { x.Operation, x.ActorId, x.RequestId }).IsUnique();
    }
}

public sealed class IdempotencyStore(AppDbContext db) : IIdempotencyStore
{
    public async Task<T?> GetResultAsync<T>(string operation, ExternalUserId actorId, Guid requestId, CancellationToken cancellationToken = default) where T : struct
    {
        var record = await db.Set<IdempotencyRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Operation == operation && x.ActorId == actorId.Value && x.RequestId == requestId, cancellationToken);

        return record is null ? null : record.ToResult<T>();
    }

    public async Task AddResultAsync<T>(string operation, ExternalUserId actorId, Guid requestId, T result, DateTimeOffset createdAt, CancellationToken cancellationToken = default) where T : struct
    {
        await db.Set<IdempotencyRecord>().AddAsync(IdempotencyRecord.Create(operation, actorId, requestId, result, createdAt), cancellationToken);
    }
}
