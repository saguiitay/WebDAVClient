using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WebDAVClient.Helpers
{
    // XmlReader's async APIs have no token parameter. Bind every underlying read
    // to the metadata operation's cancellation/deadline; the caller owns the stream.
    internal sealed class CancellationReadStream : Stream
    {
        private readonly Stream m_stream;
        private readonly CancellationToken m_cancellationToken;

        public CancellationReadStream(Stream stream, CancellationToken cancellationToken)
        {
            m_stream = stream;
            m_cancellationToken = cancellationToken;
        }

        public override bool CanRead => m_stream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var linked = cancellationToken.CanBeCanceled && cancellationToken != m_cancellationToken
                ? CancellationTokenSource.CreateLinkedTokenSource(m_cancellationToken, cancellationToken) : null;
            var token = linked?.Token ?? m_cancellationToken;
            token.ThrowIfCancellationRequested();
            return await m_stream.ReadAsync(buffer, token).AsTask().WaitAsync(token).ConfigureAwait(false);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Metadata must be read asynchronously.");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
