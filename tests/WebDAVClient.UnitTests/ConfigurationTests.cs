using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class ConfigurationTests
    {
        [TestMethod]
        public void Owned_authentication_modes_reject_invalid_upload_timeouts_with_the_public_parameter_name()
        {
            foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(-2), TimeSpan.MaxValue })
            {
                Assert.AreEqual("uploadTimeout", Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                    Client.CreateWithCredentials(null, timeout)).ParamName);
                Assert.AreEqual("uploadTimeout", Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                    Client.CreateWithBearerToken("example-token", timeout)).ParamName);
                Assert.AreEqual("uploadTimeout", Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                    Client.CreateWithBearerTokenProvider(_ => Task.FromResult("example-token"), timeout)).ParamName);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Injected_transports_reject_ineffective_client_tls_settings_before_any_request(bool directHttp)
        {
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.StatusOnly(HttpStatusCode.OK));
            using var client = directHttp ? new Client(harness.HttpClient) : new Client(harness.Wrapper);
            client.Server = "http://example.com";
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GetServerOptions());
            StringAssert.Contains(exception.Message, "injected HTTP handler");
            Assert.AreEqual(0, harness.Handler.Requests.Count);
            client.ServerCertificateValidationCallback = null;
            await client.GetServerOptions();
            Assert.AreEqual(1, harness.Handler.Requests.Count);
        }
    }
}
