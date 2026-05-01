using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Model;

namespace WebDAVClient.UnitTests.Model
{
    [TestClass]
    public class ItemTests
    {
        [TestMethod]
        public void Properties_are_independent_and_round_trip()
        {
            var created = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var modified = new DateTime(2025, 6, 7, 8, 9, 10, DateTimeKind.Utc);

            var item = new Item
            {
                Href = "/folder/file.txt",
                CreationDate = created,
                Etag = "\"abc\"",
                IsHidden = true,
                IsCollection = false,
                ContentType = "text/plain",
                LastModified = modified,
                DisplayName = "file.txt",
                ContentLength = 123L
            };

            Assert.AreEqual("/folder/file.txt", item.Href);
            Assert.AreEqual(created, item.CreationDate);
            Assert.AreEqual("\"abc\"", item.Etag);
            Assert.IsTrue(item.IsHidden);
            Assert.IsFalse(item.IsCollection);
            Assert.AreEqual("text/plain", item.ContentType);
            Assert.AreEqual(modified, item.LastModified);
            Assert.AreEqual("file.txt", item.DisplayName);
            Assert.AreEqual(123L, item.ContentLength);
        }

        [TestMethod]
        public void Defaults_are_null_or_false()
        {
            var item = new Item();

            Assert.IsNull(item.Href);
            Assert.IsNull(item.CreationDate);
            Assert.IsNull(item.Etag);
            Assert.IsFalse(item.IsHidden);
            Assert.IsFalse(item.IsCollection);
            Assert.IsNull(item.ContentType);
            Assert.IsNull(item.LastModified);
            Assert.IsNull(item.DisplayName);
            Assert.IsNull(item.ContentLength);
        }
    }
}
