using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    [DoNotParallelize]
    public class SampleCommandLineTests
    {
        [DataTestMethod]
        [DataRow("", 0, "Usage:")]
        [DataRow("--help", 0, "Usage:")]
        [DataRow("--unknown", 2, "Unknown argument")]
        [DataRow("--server", 2, "requires a value")]
        [DataRow("--server|--validate", 2, "requires a value")]
        [DataRow("--base-path", 2, "requires a value")]
        [DataRow("--target", 2, "requires a value")]
        [DataRow("--validate", 2, "HTTP(S) origin")]
        [DataRow("--server|ftp://example.com|--validate", 2, "HTTP(S) origin")]
        [DataRow("--server|https://example.com/dav/|--validate", 2, "HTTP(S) origin")]
        [DataRow("--server|https://user:pass@example.com|--validate", 2, "HTTP(S) origin")]
        [DataRow("--server|https://example.com?secret=value|--validate", 2, "HTTP(S) origin")]
        [DataRow("--server|https://example.com#fragment|--validate", 2, "HTTP(S) origin")]
        [DataRow("--server|https://example.com|--base-path| |--validate", 2, "must not be blank")]
        [DataRow("--server|https://example.com|--base-path|/dav/|--target|file|--mutate|--validate", 0, "No server contacted")]
        public async Task Help_validation_and_argument_errors_are_offline_with_documented_exit_codes(string arguments, int expected, string message)
        {
            using var environment = new CommandLineEnvironment();
            var args = arguments.Length == 0 ? Array.Empty<string>() : arguments.Split('|');
            Assert.AreEqual(expected, await TestWebDAVClient.Program.Main(args));
            StringAssert.Contains(expected == 0 ? environment.Output.ToString() : environment.Error.ToString(), message);
        }

        [DataTestMethod]
        [DataRow(null, null, null, 0)]
        [DataRow("sample-user", "sample-password", null, 0)]
        [DataRow(null, null, "sample-token", 0)]
        [DataRow(null, "sample-password", null, 2)]
        [DataRow("sample-user", null, "sample-token", 2)]
        [DataRow("", null, "", 0)]
        public async Task Environment_configuration_validates_authentication_combinations_without_network_access(string user, string password, string bearer, int expected)
        {
            using var environment = new CommandLineEnvironment();
            Environment.SetEnvironmentVariable("WEBDAV_SERVER", "https://example.com");
            Environment.SetEnvironmentVariable("WEBDAV_BASE_PATH", "/dav/");
            Environment.SetEnvironmentVariable("WEBDAV_USERNAME", user);
            Environment.SetEnvironmentVariable("WEBDAV_PASSWORD", password);
            Environment.SetEnvironmentVariable("WEBDAV_BEARER_TOKEN", bearer);
            Assert.AreEqual(expected, await TestWebDAVClient.Program.Main(new[] { "--validate" }));
            StringAssert.Contains(expected == 0 ? environment.Output.ToString() : environment.Error.ToString(),
                expected == 0 ? "No server contacted" : "authentication");
        }

        [DataTestMethod]
        [DataRow("anonymous")]
        [DataRow("credentials")]
        [DataRow("bearer")]
        public async Task Actual_entry_point_lists_and_reads_over_loopback_with_each_authentication_mode(string authentication)
        {
            using var environment = new CommandLineEnvironment();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var origin = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            Environment.SetEnvironmentVariable("WEBDAV_SERVER", origin);
            Environment.SetEnvironmentVariable("WEBDAV_BASE_PATH", "/webdav/");
            if (authentication == "credentials")
            {
                Environment.SetEnvironmentVariable("WEBDAV_USERNAME", "sample-user");
                Environment.SetEnvironmentVariable("WEBDAV_PASSWORD", "sample-password");
            }
            if (authentication == "bearer")
                Environment.SetEnvironmentVariable("WEBDAV_BEARER_TOKEN", "sample-token");
            var previousProxy = System.Net.Http.HttpClient.DefaultProxy;
            System.Net.Http.HttpClient.DefaultProxy = new WebProxy();
            int completed = 0;
            int challenged = 0;
            int expectedRequests = authentication == "credentials" ? 2 : 3;

            async Task Serve()
            {
                while (completed < expectedRequests)
                {
                    using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                    using var stream = socket.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var request = await reader.ReadLineAsync(deadline.Token);
                    if (request == null)
                        throw new IOException("Missing loopback request.");
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    while (true)
                    {
                        var line = await reader.ReadLineAsync(deadline.Token);
                        if (line == null)
                            throw new IOException("Incomplete request headers.");
                        if (line.Length == 0)
                            break;
                        var separator = line.IndexOf(':');
                        headers.Add(line.Substring(0, separator), line.Substring(separator + 1).Trim());
                    }
                    if (headers.TryGetValue("Content-Length", out var length))
                    {
                        var body = new char[int.Parse(length, CultureInfo.InvariantCulture)];
                        if (await reader.ReadBlockAsync(body.AsMemory(), deadline.Token) != body.Length)
                            throw new IOException("Incomplete request body.");
                    }
                    string status = "207 Multi-Status";
                    string extraHeaders = "";
                    byte[] content;
                    if (authentication == "credentials" && !headers.ContainsKey("Authorization"))
                    {
                        challenged++;
                        status = "401 Unauthorized";
                        extraHeaders = "WWW-Authenticate: Basic realm=\"coverage\"\r\n";
                        content = Array.Empty<byte>();
                    }
                    else
                    {
                        completed++;
                        if (authentication == "credentials")
                            Assert.AreEqual("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("sample-user:sample-password")), headers["Authorization"]);
                        else if (authentication == "bearer")
                            Assert.AreEqual("Bearer sample-token", headers["Authorization"]);
                        else
                            Assert.IsFalse(headers.ContainsKey("Authorization"));
                        if (request.StartsWith("GET ", StringComparison.Ordinal))
                        {
                            status = "200 OK";
                            content = Encoding.ASCII.GetBytes("sample content");
                        }
                        else
                            content = Encoding.ASCII.GetBytes(headers["Depth"] == "0"
                                ? WebDAVResponses.Root("/webdav/") : WebDAVResponses.FolderListing());
                    }
                    var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\n{extraHeaders}Content-Length: {content.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response, deadline.Token);
                    await stream.WriteAsync(content, deadline.Token);
                }
            }

            var server = Serve();
            try
            {
                var args = authentication == "credentials" ? Array.Empty<string>()
                    : new[] { "--server", origin, "--base-path", "/webdav/", "--target", "file.txt" };
                Assert.AreEqual(0, await TestWebDAVClient.Program.Main(args).WaitAsync(deadline.Token));
                await server.WaitAsync(deadline.Token);
                Assert.AreEqual(expectedRequests, completed);
                Assert.IsTrue(authentication != "credentials" || challenged > 0);
                StringAssert.Contains(environment.Output.ToString(), "File:");
                StringAssert.Contains(environment.Output.ToString(), "Folder:");
                if (authentication != "credentials")
                    StringAssert.Contains(environment.Output.ToString(), "Requested target read successfully.");
                Assert.AreEqual("", environment.Error.ToString());
            }
            finally
            {
                System.Net.Http.HttpClient.DefaultProxy = previousProxy;
                deadline.Cancel();
                try
                {
                    await server.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
                finally
                {
                    listener.Stop();
                }
            }
        }

        private sealed class CommandLineEnvironment : IDisposable
        {
            private readonly TextWriter m_output = Console.Out;
            private readonly TextWriter m_error = Console.Error;
            private readonly Dictionary<string, string> m_environment = new Dictionary<string, string>();
            public StringWriter Output { get; } = new StringWriter();
            public StringWriter Error { get; } = new StringWriter();

            public CommandLineEnvironment()
            {
                foreach (var name in new[] { "WEBDAV_SERVER", "WEBDAV_BASE_PATH", "WEBDAV_USERNAME", "WEBDAV_PASSWORD", "WEBDAV_BEARER_TOKEN" })
                {
                    m_environment.Add(name, Environment.GetEnvironmentVariable(name));
                    Environment.SetEnvironmentVariable(name, null);
                }
                Console.SetOut(Output);
                Console.SetError(Error);
            }

            public void Dispose()
            {
                Console.SetOut(m_output);
                Console.SetError(m_error);
                foreach (var pair in m_environment)
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                Output.Dispose();
                Error.Dispose();
            }
        }
    }
}
