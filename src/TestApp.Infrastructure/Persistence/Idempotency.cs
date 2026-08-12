using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySqlConnector;
using TestApp.Application.Abstractions;
using TestApp.Application.Common;
using TestApp.Domain.Identity;

namespace TestApp.Infrastructure.Persistence;

public sealed class IdempotencyRecord
{
    public Guid Id { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string ActorId { get; private set; } = string.Empty;
    public Guid RequestId { get; private set; }
    public string? RequestFingerprint { get; private set; }
    public string ResultType { get; private set; } = string.Empty;
    public string ResultJson { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private IdempotencyRecord() { }

    public static IdempotencyRecord Create<T>(
        string operation,
        ExternalUserId actorId,
        Guid requestId,
        string requestFingerprint,
        T result,
        DateTimeOffset createdAt) where T : struct
    {
        if (string.IsNullOrWhiteSpace(operation))
            throw new ArgumentException("Operation is required.", nameof(operation));
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request id cannot be empty.", nameof(requestId));
        ValidateFingerprint(requestFingerprint);

        return new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(),
            Operation = operation,
            ActorId = actorId.Value,
            RequestId = requestId,
            RequestFingerprint = requestFingerprint,
            ResultType = typeof(T).FullName ?? typeof(T).Name,
            ResultJson = JsonSerializer.Serialize(result, JsonSerializerOptions.Default),
            CreatedAt = createdAt
        };
    }

    public void EnsureFingerprint(string fingerprint)
    {
        ValidateFingerprint(fingerprint);
        // NULL marks records created before request fingerprints were introduced.
        // Those legacy records keep replay compatibility, while every new row is strict.
        if (RequestFingerprint is not null &&
            !string.Equals(RequestFingerprint, fingerprint, StringComparison.Ordinal))
            throw new IdempotencyKeyReuseException(Operation);
    }

    public T ToResult<T>() where T : struct =>
        JsonSerializer.Deserialize<T>(ResultJson, JsonSerializerOptions.Default);

    private static void ValidateFingerprint(string fingerprint)
    {
        if (fingerprint.Length != 64 || fingerprint.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Request fingerprint must be a 64-character SHA-256 hexadecimal value.", nameof(fingerprint));
    }
}

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_records");
        b.HasKey(x => x.Id);
        b.Property(x => x.Operation).HasMaxLength(200).IsRequired();
        b.Property(x => x.ActorId).HasMaxLength(256).IsRequired();
        b.Property(x => x.RequestFingerprint).HasMaxLength(64);
        b.Property(x => x.ResultType).HasMaxLength(512).IsRequired();
        b.Property(x => x.ResultJson).IsRequired();
        b.HasIndex(x => new { x.Operation, x.ActorId, x.RequestId }).IsUnique();
        b.HasIndex(x => x.CreatedAt);
    }
}

public sealed class IdempotencyStore(AppDbContext db) : IIdempotencyStore
{
    private const int LockTimeoutSeconds = 30;

    public async Task<IAsyncDisposable> AcquireAsync(
        string operation,
        ExternalUserId actorId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operation))
            throw new ArgumentException("Operation is required.", nameof(operation));
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request id cannot be empty.", nameof(requestId));

        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Database connection string is not configured.");
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var material = $"{connection.Database}|{operation}|{actorId.Value}|{requestId:D}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        var lockName = $"testapp:idem:{hash[..50]}";

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT GET_LOCK(@name, @timeoutSeconds);";
            command.Parameters.AddWithValue("@name", lockName);
            command.Parameters.AddWithValue("@timeoutSeconds", LockTimeoutSeconds);

            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is null || value is DBNull || Convert.ToInt32(value) != 1)
                throw new TimeoutException($"Could not acquire idempotency lease for operation '{operation}'.");

            return new MariaDbAdvisoryLockLease(connection, lockName);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<T?> GetResultAsync<T>(
        string operation,
        ExternalUserId actorId,
        Guid requestId,
        string requestFingerprint,
        CancellationToken cancellationToken = default) where T : struct
    {
        var record = await db.Set<IdempotencyRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Operation == operation && x.ActorId == actorId.Value && x.RequestId == requestId,
                cancellationToken);

        if (record is null)
            return null;

        record.EnsureFingerprint(requestFingerprint);
        return record.ToResult<T>();
    }

    public async Task AddResultAsync<T>(
        string operation,
        ExternalUserId actorId,
        Guid requestId,
        string requestFingerprint,
        T result,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default) where T : struct
    {
        await db.Set<IdempotencyRecord>().AddAsync(
            IdempotencyRecord.Create(operation, actorId, requestId, requestFingerprint, result, createdAt),
            cancellationToken);
    }

    private sealed class MariaDbAdvisoryLockLease(MySqlConnection connection, string lockName) : IAsyncDisposable
    {
        private MySqlConnection? _connection = connection;

        public async ValueTask DisposeAsync()
        {
            var current = Interlocked.Exchange(ref _connection, null);
            if (current is null)
                return;

            try
            {
                await using var command = current.CreateCommand();
                command.CommandText = "SELECT RELEASE_LOCK(@name);";
                command.Parameters.AddWithValue("@name", lockName);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                await current.DisposeAsync();
            }
        }
    }
}
