using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ew4.Persistence;

// Buffers are local to one encoding operation; returned arrays never back a save.
internal sealed class SaveBuffer : IBufferWriter<byte>, IDisposable
{
    private byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
    private int length;
    internal ReadOnlySpan<byte> WrittenSpan => buffer.AsSpan(0, length);
    public void Advance(int count)
    {
        if (count < 0 || count > buffer.Length - length) throw new ArgumentOutOfRangeException(nameof(count));
        length += count;
    }
    public Memory<byte> GetMemory(int sizeHint = 0) { Ensure(sizeHint); return buffer.AsMemory(length); }
    public Span<byte> GetSpan(int sizeHint = 0) { Ensure(sizeHint); return buffer.AsSpan(length); }
    private void Ensure(int sizeHint)
    {
        if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
        int needed = checked(length + Math.Max(1, sizeHint));
        if (needed <= buffer.Length) return;
        var next = ArrayPool<byte>.Shared.Rent(Math.Max(needed, checked(buffer.Length * 2)));
        buffer.AsSpan(0, length).CopyTo(next);
        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        buffer = next;
    }
    internal static void Envelope(Utf8JsonWriter writer, string format, int version, ReadOnlySpan<byte> payload)
    {
        writer.WriteStartObject();
        writer.WriteString("format", format); writer.WriteNumber("version", version);
        writer.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(payload)));
        writer.WritePropertyName("payload"); writer.WriteRawValue(payload, skipInputValidation: true);
        writer.WriteEndObject();
    }
    public void Dispose()
    {
        var previous = buffer; buffer = Array.Empty<byte>(); length = 0;
        if (previous.Length != 0) ArrayPool<byte>.Shared.Return(previous, clearArray: true);
    }
}
