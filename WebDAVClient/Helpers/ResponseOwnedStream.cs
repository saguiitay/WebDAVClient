using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WebDAVClient.Helpers
{
    internal sealed class ResponseOwnedStream : Stream
    {
        private readonly Stream m_stream;
        private HttpResponseMessage m_response;

        public ResponseOwnedStream(Stream stream, HttpResponseMessage response)
        {
            m_stream = stream;
            m_response = response;
        }

        private Stream Inner => m_response != null ? m_stream : throw new ObjectDisposedException(nameof(ResponseOwnedStream));
        public override bool CanRead => m_response != null && m_stream.CanRead;
        public override bool CanSeek => m_response != null && m_stream.CanSeek;
        public override bool CanWrite => m_response != null && m_stream.CanWrite;
        public override bool CanTimeout => m_response != null && m_stream.CanTimeout;
        public override long Length => Inner.Length;
        public override long Position { get => Inner.Position; set => Inner.Position = value; }
        public override int ReadTimeout { get => Inner.ReadTimeout; set => Inner.ReadTimeout = value; }
        public override int WriteTimeout { get => Inner.WriteTimeout; set => Inner.WriteTimeout = value; }
        public override void Flush() => Inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => Inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => Inner.Read(buffer);
        public override int ReadByte() => Inner.ReadByte();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Inner.ReadAsync(buffer, cancellationToken);
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            => Inner.CopyToAsync(destination, bufferSize, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);
        public override void SetLength(long value) => Inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => Inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => Inner.Write(buffer);
        public override void WriteByte(byte value) => Inner.WriteByte(value);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => Inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            // HttpContent caches and owns the stream returned by ReadAsStreamAsync.
            // Disposing only the response releases both that stream and custom content resources.
            if (disposing)
            {
                Interlocked.Exchange(ref m_response, null)?.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
