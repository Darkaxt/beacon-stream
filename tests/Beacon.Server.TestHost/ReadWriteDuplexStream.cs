namespace Beacon.Server.TestHost;

public sealed class ReadWriteDuplexStream : Stream
{
    private readonly Stream readStream;
    private readonly Stream writeStream;
    private readonly bool leaveOpen;
    private int disposed;

    public ReadWriteDuplexStream(Stream readStream, Stream writeStream, bool leaveOpen = false)
    {
        this.readStream = readStream ?? throw new ArgumentNullException(nameof(readStream));
        this.writeStream = writeStream ?? throw new ArgumentNullException(nameof(writeStream));
        if (!readStream.CanRead)
        {
            throw new ArgumentException("The duplex read stream must be readable.", nameof(readStream));
        }
        if (!writeStream.CanWrite)
        {
            throw new ArgumentException("The duplex write stream must be writable.", nameof(writeStream));
        }
        this.leaveOpen = leaveOpen;
    }

    public override bool CanRead => Volatile.Read(ref disposed) == 0 && readStream.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => Volatile.Read(ref disposed) == 0 && writeStream.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        ThrowIfDisposed();
        writeStream.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return writeStream.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfDisposed();
        return readStream.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();
        return readStream.Read(buffer);
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return readStream.ReadAsync(buffer, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ThrowIfDisposed();
        writeStream.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfDisposed();
        writeStream.Write(buffer);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return writeStream.WriteAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref disposed, 1) == 0 && !leaveOpen)
        {
            readStream.Dispose();
            if (!ReferenceEquals(readStream, writeStream))
            {
                writeStream.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0 && !leaveOpen)
        {
            await readStream.DisposeAsync().ConfigureAwait(false);
            if (!ReferenceEquals(readStream, writeStream))
            {
                await writeStream.DisposeAsync().ConfigureAwait(false);
            }
        }
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
}
