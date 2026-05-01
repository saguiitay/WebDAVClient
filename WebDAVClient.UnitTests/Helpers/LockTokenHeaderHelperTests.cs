using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;

namespace WebDAVClient.UnitTests.Helpers
{
    [TestClass]
    public class LockTokenHeaderHelperTests
    {
        // -------------------- NormalizeLockToken --------------------

        [TestMethod]
        public void NormalizeLockToken_returns_bare_token_unchanged()
        {
            Assert.AreEqual("opaquelocktoken:abc-123",
                LockTokenHeaderHelper.NormalizeLockToken("opaquelocktoken:abc-123"));
        }

        [TestMethod]
        public void NormalizeLockToken_strips_surrounding_angle_brackets()
        {
            Assert.AreEqual("opaquelocktoken:abc-123",
                LockTokenHeaderHelper.NormalizeLockToken("<opaquelocktoken:abc-123>"));
        }

        [TestMethod]
        public void NormalizeLockToken_trims_outer_whitespace()
        {
            // Real-world tokens copy-pasted from Lock-Token headers may carry trailing spaces.
            Assert.AreEqual("opaquelocktoken:abc",
                LockTokenHeaderHelper.NormalizeLockToken("  <opaquelocktoken:abc>  "));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void NormalizeLockToken_throws_on_null_or_whitespace(string input)
        {
            Assert.ThrowsException<ArgumentException>(() => LockTokenHeaderHelper.NormalizeLockToken(input));
        }

        [DataTestMethod]
        [DataRow("bad<token")]
        [DataRow("bad>token")]
        [DataRow("bad\rtoken")]
        [DataRow("bad\ntoken")]
        [DataRow("<>")]
        public void NormalizeLockToken_throws_on_malformed_payload(string input)
        {
            Assert.ThrowsException<ArgumentException>(() => LockTokenHeaderHelper.NormalizeLockToken(input));
        }

        // -------------------- BuildIfHeader --------------------

        [TestMethod]
        public void BuildIfHeader_returns_null_when_no_tokens_supplied()
        {
            Assert.IsNull(LockTokenHeaderHelper.BuildIfHeader(
                new Uri("http://x/a"), null, new Uri("http://x/b"), null));
        }

        [TestMethod]
        public void BuildIfHeader_emits_no_tag_list_when_only_source_locked()
        {
            // Source-only ⇒ no-tag-list applies to the request URI implicitly. Compact form is preferred
            // because no tagged production is needed when there's only one resource in play.
            var s = LockTokenHeaderHelper.BuildIfHeader(
                new Uri("http://x/src"), "opaquelocktoken:s",
                destinationUri: null, destinationLockToken: null);
            Assert.AreEqual("(<opaquelocktoken:s>)", s);
        }

        [TestMethod]
        public void BuildIfHeader_emits_tagged_list_when_only_destination_locked()
        {
            // Destination-only must use tagged-list pinned to the destination URI: a no-tag-list
            // would tag the request URI (the source), which is the wrong target.
            var s = LockTokenHeaderHelper.BuildIfHeader(
                new Uri("http://x/src"), sourceLockToken: null,
                new Uri("http://x/dst"), "opaquelocktoken:d");
            Assert.AreEqual("<http://x/dst> (<opaquelocktoken:d>)", s);
        }

        [TestMethod]
        public void BuildIfHeader_emits_two_tagged_productions_when_both_supplied()
        {
            var s = LockTokenHeaderHelper.BuildIfHeader(
                new Uri("http://x/src"), "opaquelocktoken:s",
                new Uri("http://x/dst"), "opaquelocktoken:d");
            Assert.AreEqual("<http://x/src> (<opaquelocktoken:s>) <http://x/dst> (<opaquelocktoken:d>)", s);
        }

        [TestMethod]
        public void BuildIfHeader_uses_AbsoluteUri_so_resource_tag_matches_Destination_header()
        {
            // AbsoluteUri preserves percent-encoding of reserved characters; ToString() would
            // unescape them and yield a tag that no longer matches the Destination header.
            var s = LockTokenHeaderHelper.BuildIfHeader(
                new Uri("http://x/src"), "opaquelocktoken:s",
                new Uri("http://x/folder%20with%20spaces/file.txt"), "opaquelocktoken:d");
            StringAssert.Contains(s, "http://x/folder%20with%20spaces/file.txt");
        }

        [TestMethod]
        public void BuildIfHeader_normalises_angle_bracketed_tokens_before_emitting()
        {
            var s = LockTokenHeaderHelper.BuildIfHeader(
                new Uri("http://x/src"), "<opaquelocktoken:s>",
                destinationUri: null, destinationLockToken: null);
            Assert.AreEqual("(<opaquelocktoken:s>)", s);
        }

        [TestMethod]
        public void BuildIfHeader_throws_when_source_token_supplied_without_request_uri()
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
                LockTokenHeaderHelper.BuildIfHeader(
                    requestUri: null, "opaquelocktoken:s",
                    new Uri("http://x/dst"), "opaquelocktoken:d"));
        }

        [TestMethod]
        public void BuildIfHeader_throws_when_destination_token_supplied_without_destination_uri()
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
                LockTokenHeaderHelper.BuildIfHeader(
                    new Uri("http://x/src"), sourceLockToken: null,
                    destinationUri: null, "opaquelocktoken:d"));
        }

        // -------------------- BuildLockTokenHeaders --------------------

        [TestMethod]
        public void BuildLockTokenHeaders_returns_null_when_no_token()
        {
            // Unlocked fast-path: no allocation, callers can pass the result straight to HttpRequest.
            Assert.IsNull(LockTokenHeaderHelper.BuildLockTokenHeaders(new Uri("http://x/a"), null));
        }

        [TestMethod]
        public void BuildLockTokenHeaders_returns_dictionary_with_If_header_when_token_supplied()
        {
            var headers = LockTokenHeaderHelper.BuildLockTokenHeaders(new Uri("http://x/a"), "opaquelocktoken:abc");
            Assert.IsNotNull(headers);
            Assert.AreEqual(1, headers.Count);
            Assert.AreEqual("(<opaquelocktoken:abc>)", headers["If"]);
        }

        // -------------------- AddIfHeader --------------------

        [TestMethod]
        public void AddIfHeader_does_not_touch_dictionary_when_no_tokens_supplied()
        {
            var headers = new Dictionary<string, string>();
            LockTokenHeaderHelper.AddIfHeader(headers, new Uri("http://x/a"),
                sourceLockToken: null, destinationUri: null, destinationLockToken: null);
            Assert.AreEqual(0, headers.Count);
        }

        [TestMethod]
        public void AddIfHeader_overwrites_existing_If_header()
        {
            // Defensive: the helper writes to headers["If"] unconditionally so a stale value
            // from a previous code path never leaks through.
            var headers = new Dictionary<string, string> { ["If"] = "stale" };
            LockTokenHeaderHelper.AddIfHeader(headers, new Uri("http://x/a"),
                "opaquelocktoken:s", destinationUri: null, destinationLockToken: null);
            Assert.AreEqual("(<opaquelocktoken:s>)", headers["If"]);
        }

        // -------------------- ExtractLockTokenHeader --------------------

        [TestMethod]
        public void ExtractLockTokenHeader_returns_null_when_header_absent()
        {
            using var response = new HttpResponseMessage();
            Assert.IsNull(LockTokenHeaderHelper.ExtractLockTokenHeader(response));
        }

        [TestMethod]
        public void ExtractLockTokenHeader_strips_angle_brackets()
        {
            using var response = new HttpResponseMessage();
            response.Headers.TryAddWithoutValidation("Lock-Token", "<opaquelocktoken:abc-123>");
            Assert.AreEqual("opaquelocktoken:abc-123", LockTokenHeaderHelper.ExtractLockTokenHeader(response));
        }

        [TestMethod]
        public void ExtractLockTokenHeader_returns_value_unchanged_when_no_brackets()
        {
            // RFC 4918 §10.5 mandates Coded-URL form, but be liberal in what we accept.
            using var response = new HttpResponseMessage();
            response.Headers.TryAddWithoutValidation("Lock-Token", "opaquelocktoken:abc-123");
            Assert.AreEqual("opaquelocktoken:abc-123", LockTokenHeaderHelper.ExtractLockTokenHeader(response));
        }

        [TestMethod]
        public void ExtractLockTokenHeader_skips_blank_values()
        {
            using var response = new HttpResponseMessage();
            response.Headers.TryAddWithoutValidation("Lock-Token", "   ");
            Assert.IsNull(LockTokenHeaderHelper.ExtractLockTokenHeader(response));
        }
    }
}
