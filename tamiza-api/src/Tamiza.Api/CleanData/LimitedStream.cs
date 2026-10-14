namespace Tamiza.Api.CleanData;

/// <summary>An export grew past the deployment's file size limit.</summary>
public sealed class ExportTooLargeException(long limit) : Exception($"The export is larger than the limit of {limit} bytes.")
{
    public long Limit { get; } = limit;
}

/// <summary>
/// Passes writes to <paramref name="inner"/> and throws <see cref="ExportTooLargeException"/> once more than
/// <paramref name="limit"/> bytes have been written, so a runaway export stops instead of filling the disk. It reports itself as
/// not seekable so that writers only append and every byte is counted once.
/// </summary>
public sealed class LimitedStream(Stream inner, long limit) : Stream
{
    public long BytesWritten { get; private set; }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesWritten;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Count(buffer.Length);
        inner.Write(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Count(buffer.Length);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private void Count(int bytes)
    {
        BytesWritten += bytes;
        if (BytesWritten > limit)
        {
            throw new ExportTooLargeException(limit);
        }
    }
}
