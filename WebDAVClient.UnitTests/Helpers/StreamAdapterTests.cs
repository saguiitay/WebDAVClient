using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;

namespace WebDAVClient.UnitTests.Helpers
{
    [TestClass]
    public class StreamAdapterTests
    {
        [TestMethod]
        public async Task Response_owned_stream_transparently_delegates_stream_operations_and_timeouts()
        {
            var inner = new TimeoutStream();
            using var response = new HttpResponseMessage { Content = new StreamContent(inner) };
            using var stream = new ResponseOwnedStream(inner, response);
            Assert.IsTrue(stream.CanRead && stream.CanSeek && stream.CanWrite && stream.CanTimeout);
            stream.ReadTimeout = 123;
            stream.WriteTimeout = 456;
            Assert.AreEqual(123, inner.ReadTimeout);
            Assert.AreEqual(456, inner.WriteTimeout);
            Assert.AreEqual(123, stream.ReadTimeout);
            Assert.AreEqual(456, stream.WriteTimeout);
            stream.Write(new byte[] { 1, 2 }, 0, 2);
            stream.Write(new ReadOnlySpan<byte>(new byte[] { 3 }));
            stream.WriteByte(4);
            await stream.WriteAsync(new byte[] { 5 }, 0, 1, CancellationToken.None);
            await stream.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 6 }));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6 }, inner.ToArray());
            Assert.AreEqual(6L, stream.Length);
            Assert.AreEqual(6L, stream.Position);
            stream.Position = 0;
            var buffer = new byte[2];
            Assert.AreEqual(2, stream.Read(buffer, 0, 2));
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, buffer);
            Assert.AreEqual(2, stream.Read(buffer.AsSpan()));
            CollectionAssert.AreEqual(new byte[] { 3, 4 }, buffer);
            Assert.AreEqual(5, stream.ReadByte());
            Assert.AreEqual(1, await stream.ReadAsync(buffer.AsMemory(1, 1)));
            Assert.AreEqual(6, buffer[1]);
            stream.Position = 0;
            Assert.AreEqual(2, await stream.ReadAsync(buffer, 0, 2, CancellationToken.None));
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, buffer);
            using var copied = new MemoryStream();
            await stream.CopyToAsync(copied, 64, CancellationToken.None);
            CollectionAssert.AreEqual(new byte[] { 3, 4, 5, 6 }, copied.ToArray());
            Assert.AreEqual(5L, stream.Seek(-1, SeekOrigin.End));
            stream.SetLength(4);
            Assert.AreEqual(4L, inner.Length);
            stream.Flush();
            await stream.FlushAsync(CancellationToken.None);
            Assert.AreEqual(1, inner.SyncFlushes);
            Assert.AreEqual(1, inner.AsyncFlushes);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Response_disposal_is_idempotent_and_closed_stream_operations_fail(bool asyncDisposal)
        {
            var inner = new MemoryStream(new byte[] { 1 });
            using var response = new CountingResponse { Content = new StreamContent(inner) };
            var stream = new ResponseOwnedStream(inner, response);
            Assert.IsFalse(stream.CanTimeout);
            if (asyncDisposal)
            {
                await stream.DisposeAsync();
            }
            else
            {
                stream.Dispose();
            }

            stream.Dispose();
            await stream.DisposeAsync();
            Assert.AreEqual(1, response.DisposeCount);
            Assert.IsFalse(inner.CanRead);
            Assert.IsFalse(stream.CanRead || stream.CanSeek || stream.CanWrite || stream.CanTimeout);
            Assert.ThrowsException<ObjectDisposedException>(() => _ = stream.Length);
            Assert.ThrowsException<ObjectDisposedException>(() => stream.Position = 0);
            Assert.ThrowsException<ObjectDisposedException>(() => stream.WriteByte(1));
        }

        [TestMethod]
        public void Response_owned_capabilities_reflect_an_inner_stream_closed_independently()
        {
            var inner = new MemoryStream();
            using var response = new HttpResponseMessage { Content = new StreamContent(inner) };
            using var stream = new ResponseOwnedStream(inner, response);
            inner.Dispose();
            Assert.IsFalse(stream.CanRead || stream.CanSeek || stream.CanWrite || stream.CanTimeout);
        }

        [DataTestMethod]
        [DataRow("none")]
        [DataRow("same")]
        [DataRow("different")]
        public async Task Cancellation_read_stream_preserves_buffers_and_combines_tokens_only_when_needed(string tokenMode)
        {
            using var bound = new CancellationTokenSource();
            using var other = new CancellationTokenSource();
            using var inner = new TokenRecordingStream(new byte[] { 1, 2, 3 });
            var token = tokenMode == "none" ? CancellationToken.None : tokenMode == "same" ? bound.Token : other.Token;
            using (var stream = new CancellationReadStream(inner, bound.Token))
            {
                Assert.IsTrue(stream.CanRead);
                Assert.IsFalse(stream.CanSeek || stream.CanWrite);
                var buffer = new byte[] { 99, 0, 0, 99 };
                Assert.AreEqual(2, await stream.ReadAsync(buffer, 1, 2, token));
                CollectionAssert.AreEqual(new byte[] { 99, 1, 2, 99 }, buffer);
                if (tokenMode != "different")
                {
                    Assert.AreEqual(bound.Token, inner.LastToken);
                }
                else
                {
                    Assert.AreNotEqual(bound.Token, inner.LastToken);
                    Assert.AreNotEqual(other.Token, inner.LastToken);
                    Assert.IsTrue(inner.LastToken.CanBeCanceled);
                }
                Assert.AreEqual(1, await stream.ReadAsync(buffer.AsMemory(1, 1), token));
                Assert.AreEqual(3, buffer[1]);
            }
            Assert.IsTrue(inner.CanRead, "The adapter must not dispose caller-owned content.");
            inner.Dispose();
            using var closedAdapter = new CancellationReadStream(inner, bound.Token);
            Assert.IsFalse(closedAdapter.CanRead);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Either_token_interrupts_an_inflight_read_even_when_the_underlying_stream_ignores_it(bool cancelBound)
        {
            using var bound = new CancellationTokenSource();
            using var perRead = new CancellationTokenSource();
            using var inner = new IgnoringCancellationStream();
            using var stream = new CancellationReadStream(inner, bound.Token);
            var pending = stream.ReadAsync(new byte[1].AsMemory(), perRead.Token).AsTask();
            await inner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            (cancelBound ? bound : perRead).Cancel();
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail("The linked read should be cancelled.");
            }
            catch (OperationCanceledException)
            {
                Assert.IsTrue(pending.IsCanceled);
            }
            finally
            {
                inner.Finish.TrySetResult(0);
            }
        }

        [TestMethod]
        public async Task Precancelled_reads_do_not_touch_content_and_synchronous_or_mutating_operations_are_unsupported()
        {
            using var cancellation = new CancellationTokenSource();
            using var inner = new TokenRecordingStream(new byte[] { 1 });
            using var stream = new CancellationReadStream(inner, cancellation.Token);
            cancellation.Cancel();
            try
            {
                _ = await stream.ReadAsync(new byte[1].AsMemory());
                Assert.Fail("Expected cancellation.");
            }
            catch (OperationCanceledException)
            {
                Assert.AreEqual(0, inner.Reads);
            }
            Action[] unsupported =
            {
                () => _ = stream.Length,
                () => _ = stream.Position,
                () => stream.Position = 0,
                () => _ = stream.Read(new byte[1], 0, 1),
                () => stream.Flush(),
                () => stream.Seek(0, SeekOrigin.Begin),
                () => stream.SetLength(1),
                () => stream.Write(new byte[1], 0, 1)
            };
            foreach (var operation in unsupported)
            {
                Assert.ThrowsException<NotSupportedException>(operation);
            }
        }

        private sealed class TimeoutStream : MemoryStream
        {
            public override bool CanTimeout => true;
            public override int ReadTimeout { get; set; }
            public override int WriteTimeout { get; set; }
            public int SyncFlushes { get; private set; }
            public int AsyncFlushes { get; private set; }
            public override void Flush() => SyncFlushes++;
            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AsyncFlushes++;
                return Task.CompletedTask;
            }
        }

        private sealed class CountingResponse : HttpResponseMessage
        {
            public int DisposeCount { get; private set; }
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeCount++;
                }

                base.Dispose(disposing);
            }
        }

        private sealed class TokenRecordingStream : MemoryStream
        {
            public TokenRecordingStream(byte[] bytes) : base(bytes) { }
            public CancellationToken LastToken { get; private set; }
            public int Reads { get; private set; }
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                LastToken = cancellationToken;
                Reads++;
                return base.ReadAsync(buffer, cancellationToken);
            }
        }

        private sealed class IgnoringCancellationStream : MemoryStream
        {
            public TaskCompletionSource Started { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<int> Finish { get; } = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                Started.TrySetResult();
                return new ValueTask<int>(Finish.Task);
            }
        }
    }
}
