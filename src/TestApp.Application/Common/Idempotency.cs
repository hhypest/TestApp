using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TestApp.Application.Common;

public sealed class IdempotencyKeyReuseException(string operation)
    : Exception($"The idempotency key has already been used with a different request for operation '{operation}'.")
{
    public string Operation { get; } = operation;
}

public static class IdempotencyFingerprint
{
    public static string Create(params string?[] parts)
    {
        using var buffer = new MemoryStream();
        foreach (var part in parts)
        {
            var bytes = Encoding.UTF8.GetBytes(part ?? string.Empty);
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            buffer.Write(length);
            buffer.Write(bytes);
        }

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    public static string Guid(Guid value) => value.ToString("N");
    public static string Instant(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    public static string OptionalInstant(DateTimeOffset? value) => value is null ? string.Empty : Instant(value.Value);
    public static string OptionalInt(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}
