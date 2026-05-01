using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml;
using WebDAVClient.Model;

namespace WebDAVClient.Helpers
{
    /// <summary>
    /// Represents the parser for response's results.
    /// </summary>
    internal static class ResponseParser
    {
        /// <summary>
        /// Parses the disk item.
        /// </summary>
        /// <param name="stream">The response text.</param>
        /// <returns>The  parsed item.</returns>
        public static Item ParseItem(Stream stream)
        {
            return ParseItems(stream).FirstOrDefault();
        }

        internal static XmlReaderSettings XmlReaderSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true
        };

        /// <summary>
        /// Parses the disk items.
        /// </summary>
        /// <param name="stream">The response text.</param>
        /// <returns>The list of parsed items.</returns>
        public static List<Item> ParseItems(Stream stream)
        {
            var items = new List<Item>();
            using (var reader = XmlReader.Create(stream, XmlReaderSettings))
            {
                Item itemInfo = null;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        var localName = reader.LocalName;
                        if (string.Equals(localName, "response", StringComparison.OrdinalIgnoreCase))
                        {
                            itemInfo = new Item();
                        }
                        else if (string.Equals(localName, "href", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                var value = reader.Value;
                                value = value.Replace("#", "%23");
                                itemInfo.Href = value;
                            }
                        }
                        else if (string.Equals(localName, "creationdate", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                if (DateTime.TryParse(reader.Value, out var creationDate))
                                {
                                    itemInfo.CreationDate = creationDate;
                                }
                            }
                        }
                        else if (string.Equals(localName, "getlastmodified", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                if (DateTime.TryParse(reader.Value, out var lastModified))
                                {
                                    itemInfo.LastModified = lastModified;
                                }
                            }
                        }
                        else if (string.Equals(localName, "displayname", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                itemInfo.DisplayName = reader.Value;
                            }
                        }
                        else if (string.Equals(localName, "getcontentlength", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                if (long.TryParse(reader.Value, out long contentLength))
                                {
                                    itemInfo.ContentLength = contentLength;
                                }
                            }
                        }
                        else if (string.Equals(localName, "getcontenttype", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                itemInfo.ContentType = reader.Value;
                            }
                        }
                        else if (string.Equals(localName, "getetag", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                itemInfo.Etag = reader.Value;
                            }
                        }
                        else if (string.Equals(localName, "iscollection", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                if (bool.TryParse(reader.Value, out bool isCollection))
                                {
                                    itemInfo.IsCollection = isCollection;
                                }
                                if (int.TryParse(reader.Value, out int isCollectionInt))
                                {
                                    itemInfo.IsCollection = isCollectionInt == 1;
                                }
                            }
                        }
                        else if (string.Equals(localName, "resourcetype", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                reader.Read();
                                if (string.Equals(reader.LocalName, "collection", StringComparison.OrdinalIgnoreCase))
                                {
                                    itemInfo.IsCollection = true;
                                }
                            }
                        }
                        else if (string.Equals(localName, "hidden", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(localName, "ishidden", StringComparison.OrdinalIgnoreCase))
                        {
                            itemInfo.IsHidden = true;
                        }
                        else if (string.Equals(localName, "checked-in", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(localName, "version-controlled-configuration", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Skip();
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement && 
                        string.Equals(reader.LocalName, "response", StringComparison.OrdinalIgnoreCase))
                    {
                        // Remove trailing / if the item is not a collection
                        var href = itemInfo.Href.TrimEnd('/');
                        if (!itemInfo.IsCollection)
                        {
                            itemInfo.Href = href;
                        }
                        if (string.IsNullOrEmpty(itemInfo.DisplayName) )
                        {
                            var name = href.Substring(href.LastIndexOf('/') + 1);
                            itemInfo.DisplayName = WebUtility.UrlDecode(name);
                        }
                        items.Add(itemInfo);
                    }
                }
            }

            return items;
        }
    }
}
