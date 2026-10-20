using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DavLoopbackTests
    {
        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task Real_transport_roundtrips_binary_uploads_with_correct_framing_ownership_and_connection_reuse(bool seekable, bool disposeContent)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/dav/");
            var payload = Enumerable.Range(0, 8192).Select(i => (byte)(i % 256)).ToArray();
            var expected = payload.Skip(3).ToArray();
            int requests = 0;

            async Task Serve()
            {
                using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                using var stream = socket.GetStream();
                var put = await ReadRequestAsync(stream, deadline.Token);
                requests++;
                Assert.AreEqual("PUT /dav/binary HTTP/1.1", put.Line);
                Assert.AreEqual(seekable, put.Headers.ContainsKey("Content-Length"));
                Assert.AreEqual(!seekable, put.Headers.ContainsKey("Transfer-Encoding"));
                CollectionAssert.AreEqual(expected, put.Body);
                await WriteResponseAsync(stream, "201 Created", Array.Empty<byte>(), "", deadline.Token);

                var get = await ReadRequestAsync(stream, deadline.Token);
                requests++;
                Assert.AreEqual("GET /dav/binary HTTP/1.1", get.Line);
                Assert.AreEqual(0, get.Body.Length);
                await WriteResponseAsync(stream, "200 OK", put.Body, "Content-Type: application/octet-stream\r\nETag: \"binary\"\r\n", deadline.Token);
            }

            var server = Serve();
            try
            {
                using var client = new DavClient(endpoint);
                using var input = new UploadStream(payload, seekable) { Position = 3 };
                var uploaded = await client.UploadAsync(DavPath.FromSegments("binary"), input,
                    new UploadOptions { DisposeContent = disposeContent }, deadline.Token);
                uploaded.EnsureSucceeded();
                Assert.AreEqual(disposeContent, input.IsDisposed);
                using var download = await client.DownloadAsync(DavPath.FromSegments("binary"), cancellationToken: deadline.Token);
                using var received = new MemoryStream();
                await download.Content.CopyToAsync(received, deadline.Token);
                CollectionAssert.AreEqual(expected, received.ToArray());
                Assert.AreEqual("\"binary\"", download.ETag);
                Assert.AreEqual((long)expected.Length, download.ContentLength);
                await server.WaitAsync(deadline.Token);
                Assert.AreEqual(2, requests, "Both requests must use the single accepted TCP connection.");
            }
            finally
            {
                await StopServerAsync(listener, deadline, server);
            }
        }

        [TestMethod]
        public async Task Owned_transport_does_not_follow_redirects_store_cookies_or_automatically_decode_content()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/dav/");
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(Encoding.UTF8.GetBytes("encoded representation"));
            }
            var body = compressed.ToArray();

            async Task Serve()
            {
                using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                using var stream = socket.GetStream();
                var first = await ReadRequestAsync(stream, deadline.Token);
                Assert.AreEqual("OPTIONS /dav/ HTTP/1.1", first.Line);
                Assert.IsFalse(first.Headers.ContainsKey("Authorization"));
                await WriteResponseAsync(stream, "302 Found", Array.Empty<byte>(), "Location: /followed\r\nSet-Cookie: session=unexpected; Path=/\r\n", deadline.Token);
                var second = await ReadRequestAsync(stream, deadline.Token);
                Assert.AreEqual("HEAD /dav/ HTTP/1.1", second.Line, "A redirect must not produce a follow-up request.");
                Assert.IsFalse(second.Headers.ContainsKey("Cookie"));
                await WriteResponseAsync(stream, "200 OK", Array.Empty<byte>(), "", deadline.Token);
                var third = await ReadRequestAsync(stream, deadline.Token);
                Assert.AreEqual("GET /dav/ HTTP/1.1", third.Line);
                Assert.IsFalse(third.Headers.ContainsKey("Accept-Encoding"));
                await WriteResponseAsync(stream, "200 OK", body, "Content-Encoding: gzip\r\n", deadline.Token);
            }

            var server = Serve();
            try
            {
                using var client = new DavClient(endpoint);
                Assert.AreEqual(302, (await client.GetCapabilitiesAsync(DavPath.Root, cancellationToken: deadline.Token)).StatusCode);
                (await client.HeadAsync(DavPath.Root, cancellationToken: deadline.Token)).EnsureHttpSuccess();
                using var download = await client.DownloadAsync(DavPath.Root, cancellationToken: deadline.Token);
                using var received = new MemoryStream();
                await download.Content.CopyToAsync(received, deadline.Token);
                CollectionAssert.AreEqual(body, received.ToArray());
                Assert.AreEqual("gzip", download.GetHeader("Content-Encoding"));
                await server.WaitAsync(deadline.Token);
            }
            finally
            {
                await StopServerAsync(listener, deadline, server);
            }
        }

        private static async Task<(string Line, Dictionary<string, string> Headers, byte[] Body)> ReadRequestAsync(NetworkStream stream, CancellationToken token)
        {
            var line = await ReadLineAsync(stream, token);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var header = await ReadLineAsync(stream, token);
                if (header.Length == 0) { break; }
                int colon = header.IndexOf(':');
                Assert.IsTrue(colon > 0);
                headers.Add(header.Substring(0, colon), header.Substring(colon + 1).Trim());
            }
            using var body = new MemoryStream();
            if (headers.TryGetValue("Content-Length", out var length))
            {
                int size = int.Parse(length, CultureInfo.InvariantCulture);
                Assert.IsTrue(size >= 0 && size <= 65536);
                var bytes = new byte[size];
                await stream.ReadExactlyAsync(bytes, token);
                body.Write(bytes);
            }
            else if (headers.TryGetValue("Transfer-Encoding", out var coding))
            {
                Assert.AreEqual("chunked", coding);
                while (true)
                {
                    int size = int.Parse(await ReadLineAsync(stream, token), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    Assert.IsTrue(size >= 0 && body.Length + size <= 65536);
                    if (size == 0)
                    {
                        Assert.AreEqual("", await ReadLineAsync(stream, token));
                        break;
                    }
                    var bytes = new byte[size];
                    await stream.ReadExactlyAsync(bytes, token);
                    body.Write(bytes);
                    Assert.AreEqual("", await ReadLineAsync(stream, token));
                }
            }
            return (line, headers, body.ToArray());
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken token)
        {
            var line = new StringBuilder();
            var buffer = new byte[1];
            while (true)
            {
                await stream.ReadExactlyAsync(buffer, token);
                if (buffer[0] == '\n')
                {
                    Assert.IsTrue(line.Length > 0 && line[line.Length - 1] == '\r');
                    return line.ToString(0, line.Length - 1);
                }
                line.Append((char)buffer[0]);
                Assert.IsTrue(line.Length <= 16384);
            }
        }

        private static async Task WriteResponseAsync(NetworkStream stream, string status, byte[] body, string extraHeaders, CancellationToken token)
        {
            var headers = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Length: " + body.Length + "\r\n" + extraHeaders + "\r\n");
            await stream.WriteAsync(headers, token);
            await stream.WriteAsync(body, token);
        }

        private static async Task StopServerAsync(TcpListener listener, CancellationTokenSource deadline, Task server)
        {
            deadline.Cancel();
            listener.Stop();
            try { await server.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            catch (IOException) when (deadline.IsCancellationRequested) { }
            catch (SocketException) when (deadline.IsCancellationRequested) { }
        }

        private sealed class UploadStream : MemoryStream
        {
            private readonly bool m_seekable;
            public UploadStream(byte[] bytes, bool seekable) : base(bytes) { m_seekable = seekable; }
            public override bool CanSeek => m_seekable && base.CanSeek;
            public bool IsDisposed { get; private set; }
            protected override void Dispose(bool disposing)
            {
                if (disposing) { IsDisposed = true; }
                base.Dispose(disposing);
            }
        }
    }
}
