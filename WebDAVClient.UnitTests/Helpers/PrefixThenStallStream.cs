using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebDAVClient.UnitTests.Helpers
{
    internal sealed class PrefixThenStallStream : Stream
    {
        private readonly byte[] m_prefix;
        private int m_position;
        public TaskCompletionSource Stalled { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsDisposed { get; private set; }

        public PrefixThenStallStream(string prefix)
        {
            m_prefix = Encoding.UTF8.GetBytes(prefix);
        }

        public override bool CanRead => !IsDisposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length == 0)
                return 0;
            if (m_position < m_prefix.Length)
            {
                int count = Math.Min(buffer.Length, m_prefix.Length - m_position);
                m_prefix.AsMemory(m_position, count).CopyTo(buffer);
                m_position += count;
                return count;
            }
            Stalled.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Network reads must be asynchronous.");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
