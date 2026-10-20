using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class ConnectionReuseTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public async Task Unread_mutation_and_failed_download_bodies_release_a_single_connection()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int connections = 0;
            int requests = 0;
            var root = Encoding.ASCII.GetBytes(WebDAVResponses.Root("/webdav/"));
            var unusedBody = new byte[64 * 1024];
            Array.Fill(unusedBody, (byte)'x');

            async Task Serve()
            {
                while (!deadline.IsCancellationRequested)
                {
                    using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                    Interlocked.Increment(ref connections);
                    using var stream = socket.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    while (true)
                    {
                        var requestLine = await reader.ReadLineAsync(deadline.Token);
                        if (requestLine == null)
                        {
                            break;
                        }

                        string method = requestLine.Split(' ')[0];
                        int contentLength = 0;
                        while (true)
                        {
                            var header = await reader.ReadLineAsync(deadline.Token);
                            if (header == null)
                            {
                                throw new IOException("Unexpected end of request headers.");
                            }

                            if (header.Length == 0)
                            {
                                break;
                            }

                            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                contentLength = int.Parse(header.Substring("Content-Length:".Length), CultureInfo.InvariantCulture);
                            }
                        }
                        // These test requests contain only ASCII XML/MOVE/COPY bodies.
                        var requestBody = new char[contentLength];
                        if (contentLength > 0 && await reader.ReadBlockAsync(requestBody.AsMemory(), deadline.Token) != contentLength)
                        {
                            throw new IOException("Unexpected end of request content.");
                        }

                        Interlocked.Increment(ref requests);
                        var body = method == "PROPFIND" ? root : unusedBody;
                        var status = method == "PROPFIND" ? "207 Multi-Status"
                            : method == "GET" ? "403 Forbidden" : "201 Created";
                        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\nConnection: keep-alive\r\n\r\n");
                        await stream.WriteAsync(headers, deadline.Token);
                        await stream.WriteAsync(body, deadline.Token);
                        await stream.FlushAsync(deadline.Token);
                    }
                }
            }

            var server = Serve();
            var timer = Stopwatch.StartNew();
            try
            {
                using var handler = new SocketsHttpHandler
                {
                    UseProxy = false,
                    MaxConnectionsPerServer = 1,
                    MaxResponseDrainSize = 1024 * 1024,
                    ResponseDrainTimeout = TimeSpan.FromSeconds(2)
                };
                using var http = new System.Net.Http.HttpClient(handler);
                using var client = new Client(http)
                {
                    Server = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port,
                    BasePath = "/webdav/"
                };
                await client.GetFolder(cancellationToken: deadline.Token);
                for (int i = 0; i < 4; i++)
                {
                    Assert.IsTrue(await client.MoveFile("source", "destination", cancellationToken: deadline.Token));
                    Assert.IsTrue(await client.CopyFile("source", "destination", cancellationToken: deadline.Token));
                    var error = await Assert.ThrowsExceptionAsync<WebDAVException>(
                        () => client.Download("missing", cancellationToken: deadline.Token));
                    Assert.AreEqual(403, error.GetHttpCode());
                }
                await client.GetFolder(cancellationToken: deadline.Token);
                timer.Stop();
                Assert.AreEqual(15, Volatile.Read(ref requests));
                Assert.AreEqual(1, Volatile.Read(ref connections));
                TestContext.WriteLine($"REUSE requests={requests}; accepted_connections={connections}; max_connections_per_server=1; ignored_body_bytes={unusedBody.Length}; elapsed_ms={timer.Elapsed.TotalMilliseconds:F3}");
            }
            finally
            {
                deadline.Cancel();
                try
                {
                    await server.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
                catch (IOException) when (deadline.IsCancellationRequested) { }
                catch (SocketException) when (deadline.IsCancellationRequested) { }
                finally
                {
                    listener.Stop();
                }
            }
        }
    }
}
