using System.Text;

internal sealed class GatedStream : Stream
{
    private int _part;
    internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_part >= 2) return 0;
        if (_part == 1) await Finish.Task.WaitAsync(cancellationToken);
        var text = _part++ == 0 ? "data: {\"type\":\"response.output_text.delta\",\"delta\":\"first\"}\n\n" : "data: {\"type\":\"response.completed\"}\n\n";
        return Encoding.UTF8.GetBytes(text, buffer.Span);
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
