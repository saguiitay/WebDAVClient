using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.Transfers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DavFileTransfersTests
    {
        private const int c_mib = 1024 * 1024;

        private readonly string m_directory = Path.Combine(Environment.CurrentDirectory, "transfer-tests-" + Guid.NewGuid().ToString("N"));

        private string Destination => Path.Combine(m_directory, "download.bin");

        [TestInitialize]
        public void Initialize()
        {
            Directory.CreateDirectory(m_directory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Directory.Delete(m_directory, true);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(17)]
        [DataRow(2200000)]
        public async Task Publishes_exact_identity_bytes_with_bounded_nonseekable_stream_and_retains_client(int length)
        {
            var source = new GeneratedStream(length);
            using var handler = new StubHttpMessageHandler(request =>
            {
                if (request.RequestUri == DavClientTests.Endpoint)
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                Assert.AreEqual("identity", request.Headers.AcceptEncoding.Single().Value);
                Assert.IsNull(request.Headers.Range);
                return Response(source, length);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using (var client = new DavClient(DavClientTests.Endpoint, http))
            {
                await client.DownloadFileAsync(DavClientTests.File, Destination);
            }
            CollectionAssert.AreEqual(Bytes(length), await File.ReadAllBytesAsync(Destination));
            Assert.IsTrue(source.Disposed);
            Assert.IsTrue(source.LargestRead <= 64 * 1024);
            Assert.IsFalse(File.Exists(Destination + ".davpart"));
            Assert.IsFalse(File.Exists(Destination + ".davcheckpoint"));
            Assert.IsTrue(File.Exists(Destination + ".davlock"));
            using var stillOwned = await http.GetAsync(DavClientTests.Endpoint);
        }

        [TestMethod]
        public async Task Interrupted_body_resumes_durable_prefix_and_truncates_uncommitted_tail()
        {
            const int length = 2 * c_mib + 17;
            var first = new GeneratedStream(length, faultAfter: c_mib + 123456);
            var second = new GeneratedStream(length, start: c_mib);
            using var handler = new StubHttpMessageHandler(request =>
            {
                if (request.Headers.Range == null)
                {
                    return Response(first, length);
                }
                Assert.AreEqual("bytes=1048576-", request.Headers.Range.ToString());
                Assert.AreEqual("\"stable\"", request.Headers.IfMatch.Single().ToString());
                Assert.AreEqual("\"stable\"", request.Headers.IfRange.ToString());
                Assert.AreEqual(c_mib, new FileInfo(Destination + ".davpart").Length);
                return Response(second, length - c_mib, c_mib, length);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            await Assert.ThrowsExceptionAsync<IOException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            Assert.IsTrue(first.Disposed);
            Assert.IsFalse(File.Exists(Destination));
            await client.DownloadFileAsync(DavClientTests.File, Destination);
            CollectionAssert.AreEqual(Bytes(length), await File.ReadAllBytesAsync(Destination));
            Assert.IsTrue(second.Disposed);
            Assert.AreEqual(2, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow("missing")]
        [DataRow("weak")]
        [DataRow("wildcard")]
        [DataRow("length")]
        [DataRow("short")]
        [DataRow("long")]
        [DataRow("encoding")]
        [DataRow("status")]
        public async Task Invalid_initial_responses_never_publish_and_dispose_body(string mode)
        {
            var source = new GeneratedStream(mode == "short" ? 2 : 4);
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = Response(source, mode == "long" ? 2 : 4);
                switch (mode)
                {
                    case "missing":
                        response.Headers.ETag = null;
                        break;
                    case "weak":
                        response.Headers.ETag = new EntityTagHeaderValue("\"weak\"", true);
                        break;
                    case "wildcard":
                        response.Headers.ETag = EntityTagHeaderValue.Any;
                        break;
                    case "length":
                        response.Content.Headers.ContentLength = null;
                        break;
                    case "encoding":
                        response.Content.Headers.ContentEncoding.Add("gzip");
                        break;
                    case "status":
                        response.StatusCode = HttpStatusCode.NotFound;
                        break;
                }
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            if (mode == "encoding" || mode == "status")
            {
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            }
            else
            {
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            }
            Assert.IsFalse(File.Exists(Destination));
            Assert.IsTrue(source.Disposed);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow("ignored")]
        [DataRow("changed")]
        [DataRow("missing")]
        [DataRow("total")]
        [DataRow("unknown-total")]
        [DataRow("start")]
        [DataRow("end")]
        [DataRow("length")]
        [DataRow("malformed")]
        [DataRow("encoding")]
        [DataRow("precondition")]
        [DataRow("unsatisfiable")]
        public async Task Resume_rejections_never_append_or_publish(string mode)
        {
            await WriteStateAsync(length: 20, offset: 10);
            var source = new GeneratedStream(20, start: 10);
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = Response(source, 10, 10, 20);
                switch (mode)
                {
                    case "ignored":
                        response.StatusCode = HttpStatusCode.OK;
                        break;
                    case "changed":
                        response.Headers.ETag = new EntityTagHeaderValue("\"changed\"");
                        break;
                    case "missing":
                        response.Headers.ETag = null;
                        break;
                    case "total":
                        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(10, 20, 21);
                        response.Content.Headers.ContentLength = 11;
                        break;
                    case "unknown-total":
                        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(10, 19);
                        break;
                    case "start":
                        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(9, 19, 20);
                        break;
                    case "end":
                        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(10, 18, 20);
                        break;
                    case "length":
                        response.Content.Headers.ContentLength = 9;
                        break;
                    case "malformed":
                        response.Content.Headers.Remove("Content-Range");
                        response.Content.Headers.TryAddWithoutValidation("Content-Range", "rubbish");
                        break;
                    case "encoding":
                        response.Content.Headers.ContentEncoding.Add("gzip");
                        break;
                    case "precondition":
                        response.StatusCode = HttpStatusCode.PreconditionFailed;
                        break;
                    case "unsatisfiable":
                        response.StatusCode = HttpStatusCode.RequestedRangeNotSatisfiable;
                        break;
                }
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            if (mode == "total" || mode == "missing")
            {
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            }
            else
            {
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            }
            CollectionAssert.AreEqual(Bytes(10), await File.ReadAllBytesAsync(Destination + ".davpart"));
            Assert.IsFalse(File.Exists(Destination));
            Assert.IsTrue(source.Disposed);
        }

        [DataTestMethod]
        [DataRow("short-file")]
        [DataRow("prefix")]
        [DataRow("checksum")]
        [DataRow("delimiter")]
        [DataRow("small")]
        [DataRow("oversized")]
        [DataRow("json")]
        [DataRow("null")]
        [DataRow("version")]
        [DataRow("uri")]
        [DataRow("weak")]
        [DataRow("negative-length")]
        [DataRow("negative-offset")]
        [DataRow("offset")]
        [DataRow("digest-null")]
        [DataRow("digest-short")]
        [DataRow("complete")]
        [DataRow("partial-only")]
        [DataRow("checkpoint-only")]
        public async Task Corrupt_or_unrelated_state_fails_before_network(string mode)
        {
            await WriteStateAsync(20, 10);
            switch (mode)
            {
                case "short-file":
                    File.WriteAllBytes(Destination + ".davpart", Bytes(9));
                    break;
                case "prefix":
                    File.WriteAllBytes(Destination + ".davpart", new byte[10]);
                    break;
                case "checksum":
                    File.AppendAllText(Destination + ".davcheckpoint", " ");
                    break;
                case "delimiter":
                    var data = await File.ReadAllBytesAsync(Destination + ".davcheckpoint");
                    data[64] = 32;
                    await File.WriteAllBytesAsync(Destination + ".davcheckpoint", data);
                    break;
                case "small":
                    File.WriteAllBytes(Destination + ".davcheckpoint", Array.Empty<byte>());
                    break;
                case "oversized":
                    File.WriteAllBytes(Destination + ".davcheckpoint", new byte[16385]);
                    break;
                case "json":
                    await WritePayloadAsync("{");
                    break;
                case "null":
                    await WritePayloadAsync("null");
                    break;
                case "partial-only":
                    File.Delete(Destination + ".davcheckpoint");
                    break;
                case "checkpoint-only":
                    File.Delete(Destination + ".davpart");
                    break;
                default:
                    await WriteStateAsync(mode == "negative-length" ? -1 : 20,
                        mode == "negative-offset" ? -1 : mode == "offset" ? 21 : 10,
                        complete: mode == "complete", version: mode == "version" ? 2 : 1,
                        uri: mode == "uri" ? "https://other.invalid/" : null,
                        etag: mode == "weak" ? "W/\"weak\"" : "\"stable\"",
                        digest: mode == "digest-null" ? null : mode == "digest-short" ? "ABC" : "normal",
                        createPartial: false);
                    break;
            }
            using var handler = new StubHttpMessageHandler(_ => throw new AssertFailedException("Network must not be reached."));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            Assert.IsFalse(File.Exists(Destination));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Durable_completed_checkpoint_publishes_without_network_after_crash()
        {
            await WriteStateAsync(10, 10, complete: true);
            using var handler = new StubHttpMessageHandler(_ => throw new AssertFailedException("Completed checkpoint must not request data."));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            await client.DownloadFileAsync(DavClientTests.File, Destination);
            CollectionAssert.AreEqual(Bytes(10), await File.ReadAllBytesAsync(Destination));
        }

        [TestMethod]
        public async Task Publication_conflict_preserves_completed_checkpoint_for_network_free_recovery()
        {
            var source = new GeneratedStream(20, onEof: () => File.WriteAllText(Destination, "competing file"));
            using var handler = new StubHttpMessageHandler(_ => Response(source, 20));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            await Assert.ThrowsExceptionAsync<IOException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            Assert.AreEqual("competing file", await File.ReadAllTextAsync(Destination));
            Assert.IsTrue(source.Disposed);
            File.Delete(Destination);
            await client.DownloadFileAsync(DavClientTests.File, Destination);
            CollectionAssert.AreEqual(Bytes(20), await File.ReadAllBytesAsync(Destination));
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Exact_length_without_validated_eof_is_never_published_and_can_restart()
        {
            var source = new GeneratedStream(20, faultAfter: 20);
            int requests = 0;
            using var handler = new StubHttpMessageHandler(_ => Response(requests++ == 0 ? source : new GeneratedStream(20), 20));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            await Assert.ThrowsExceptionAsync<IOException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            Assert.IsFalse(File.Exists(Destination));
            await client.DownloadFileAsync(DavClientTests.File, Destination);
            CollectionAssert.AreEqual(Bytes(20), await File.ReadAllBytesAsync(Destination));
        }

        [TestMethod]
        public async Task Resumed_chunked_identity_range_validates_body_against_total()
        {
            await WriteStateAsync(20, 10);
            using var handler = new StubHttpMessageHandler(_ =>
            {
                var response = Response(new GeneratedStream(20, start: 10), null, 10, 20);
                response.Content.Headers.ContentEncoding.Add("identity");
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            await client.DownloadFileAsync(DavClientTests.File, Destination);
            CollectionAssert.AreEqual(Bytes(20), await File.ReadAllBytesAsync(Destination));
        }

        [TestMethod]
        public async Task Cancellation_at_eof_never_publishes_and_remains_recoverable()
        {
            using var cancellation = new CancellationTokenSource();
            using var handler = new StubHttpMessageHandler(_ =>
                Response(new GeneratedStream(20, onEof: () => cancellation.Cancel()), 20));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            try
            {
                await client.DownloadFileAsync(DavClientTests.File, Destination, cancellation.Token);
                Assert.Fail("Expected cancellation.");
            }
            catch (OperationCanceledException)
            {
                Assert.IsTrue(cancellation.IsCancellationRequested);
            }
            Assert.IsFalse(File.Exists(Destination));
            Assert.IsTrue(File.Exists(Destination + ".davcheckpoint"));
        }

        [DataTestMethod]
        [DataRow("etag")]
        [DataRow("length")]
        [DataRow("success")]
        public async Task Zero_offset_restart_is_conditional_full_get(string mode)
        {
            await WriteStateAsync(4, 0);
            using var handler = new StubHttpMessageHandler(request =>
            {
                Assert.IsNull(request.Headers.Range);
                Assert.AreEqual("\"stable\"", request.Headers.IfMatch.Single().ToString());
                var response = Response(new GeneratedStream(4), mode == "length" ? 5 : 4);
                if (mode == "etag")
                {
                    response.Headers.ETag = new EntityTagHeaderValue("\"changed\"");
                }
                return response;
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            if (mode == "success")
            {
                await client.DownloadFileAsync(DavClientTests.File, Destination);
                CollectionAssert.AreEqual(Bytes(4), await File.ReadAllBytesAsync(Destination));
            }
            else
            {
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            }
        }

        [TestMethod]
        public async Task Cancellation_preserves_checkpoint_disposes_response_and_allows_restart()
        {
            using var cancellation = new CancellationTokenSource();
            var source = new GeneratedStream(2 * c_mib, faultAfter: c_mib, onFault: () => cancellation.Cancel());
            using var handler = new StubHttpMessageHandler(_ => Response(source, 2 * c_mib));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => client.DownloadFileAsync(DavClientTests.File, Destination, cancellation.Token));
            Assert.IsTrue(source.Disposed);
            Assert.IsTrue(File.Exists(Destination + ".davcheckpoint"));
            Assert.IsFalse(File.Exists(Destination));
        }

        [TestMethod]
        public async Task Invalid_arguments_existing_destination_and_concurrent_use_are_non_destructive()
        {
            using var handler = new StubHttpMessageHandler(_ => throw new AssertFailedException("Must fail before network."));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => DavFileTransfers.DownloadFileAsync(null, DavClientTests.File, Destination));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.DownloadFileAsync(DavClientTests.File, ""));
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => client.DownloadFileAsync(null, Destination));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => client.DownloadFileAsync(DavClientTests.File, Destination, cancelled.Token));
            using (var gate = new FileStream(Destination + ".davlock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                await Assert.ThrowsExceptionAsync<IOException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            }
            await File.WriteAllTextAsync(Destination, "keep");
            await Assert.ThrowsExceptionAsync<IOException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            Assert.AreEqual("keep", await File.ReadAllTextAsync(Destination));
        }

        [TestMethod]
        public async Task Logical_representation_larger_than_int32_keeps_long_range_and_length()
        {
            const long total = (long)uint.MaxValue + 1024;
            await WriteStateAsync(total, 10);
            using var handler = new StubHttpMessageHandler(request =>
            {
                Assert.AreEqual(10L, request.Headers.Range.Ranges.Single().From);
                return Response(new GeneratedStream(total, start: 10, faultAfter: 10), total - 10, 10, total);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            await Assert.ThrowsExceptionAsync<IOException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            Assert.IsTrue(File.ReadAllText(Destination + ".davcheckpoint").Contains(total.ToString()));
        }

        [TestMethod]
        public async Task Logical_offsets_beyond_four_gibibytes_are_not_truncated_to_int32()
        {
            const long offset = (long)uint.MaxValue + 100;
            const long total = offset + 20;
            using var handler = new StubHttpMessageHandler(request =>
            {
                Assert.AreEqual(offset, request.Headers.Range.Ranges.Single().From);
                return Response(new GeneratedStream(total, start: offset), 20, offset, total);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            using (var response = await client.DownloadAsync(DavClientTests.File,
                new DownloadOptions { Range = new RangeHeaderValue(offset, null), IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"stable\"")) }))
            {
                using var bytes = new MemoryStream();
                await response.Content.CopyToAsync(bytes);
                CollectionAssert.AreEqual(Enumerable.Range(0, 20).Select(i => (byte)((offset + i) % 251)).ToArray(), bytes.ToArray());
            }
            await WriteStateAsync(total, offset);
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.DownloadFileAsync(DavClientTests.File, Destination));
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Oversized_checkpoint_identity_is_explicit_and_does_not_publish()
        {
            using var handler = new StubHttpMessageHandler(_ => Response(new GeneratedStream(1), 1));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(DavClientTests.Endpoint, http);
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.DownloadFileAsync(DavPath.FromSegments(new string('x', 17000)), Destination));
            Assert.IsFalse(File.Exists(Destination));
        }

        private async Task WriteStateAsync(long length, long offset, bool complete = false, int version = 1,
            string uri = null, string etag = "\"stable\"", string digest = "normal", bool createPartial = true)
        {
            var bytes = Bytes((int)Math.Max(0, Math.Min(offset, 100)));
            if (createPartial)
            {
                await File.WriteAllBytesAsync(Destination + ".davpart", bytes);
            }
            await WritePayloadAsync(JsonSerializer.Serialize(new
            {
                Version = version,
                Uri = uri ?? new Uri(DavClientTests.Endpoint, "file").AbsoluteUri,
                ETag = etag,
                Length = length,
                Offset = offset,
                Sha256 = digest == "normal" ? Convert.ToHexString(SHA256.HashData(bytes)) : digest,
                Complete = complete
            }));
        }

        private async Task WritePayloadAsync(string payload)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            await File.WriteAllTextAsync(Destination + ".davcheckpoint", hash + "\n" + payload, new UTF8Encoding(false));
        }

        private static HttpResponseMessage Response(Stream source, long? length, long? start = null, long total = 0)
        {
            var response = new HttpResponseMessage(start.HasValue ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(source)
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
            response.Content.Headers.ContentLength = length;
            if (start.HasValue)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start.Value, total - 1, total);
            }
            return response;
        }

        private static byte[] Bytes(int length) => Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();

        private sealed class GeneratedStream : Stream
        {
            private readonly long m_end;

            private readonly long m_faultAfter;

            private readonly Action m_onFault;

            private readonly Action m_onEof;

            private long m_position;

            public GeneratedStream(long end, long start = 0, long faultAfter = long.MaxValue, Action onFault = null, Action onEof = null)
            {
                m_end = end;
                m_position = start;
                m_faultAfter = faultAfter;
                m_onFault = onFault;
                m_onEof = onEof;
            }

            public bool Disposed
            {
                get; private set;
            }

            public int LargestRead
            {
                get; private set;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException(); set => throw new NotSupportedException();
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_position >= m_faultAfter)
                {
                    m_onFault?.Invoke();
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new IOException("Simulated interrupted response.");
                }
                LargestRead = Math.Max(LargestRead, buffer.Length);
                int count = (int)Math.Min(buffer.Length, m_end - m_position);
                if (count == 0)
                {
                    m_onEof?.Invoke();
                }
                for (int i = 0; i < count; i++)
                {
                    buffer.Span[i] = (byte)((m_position + i) % 251);
                }
                m_position += count;
                return ValueTask.FromResult(count);
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override void Flush() => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
