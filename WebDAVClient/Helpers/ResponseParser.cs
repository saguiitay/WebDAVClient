﻿using System;
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
                        switch (reader.LocalName.ToLower())
                        {
                            case "response":
                                itemInfo = new Item();
                                break;
                            case "href":
                                if (!reader.IsEmptyElement)
                                {
                                    reader.Read();
                                    var value = reader.Value;
                                    value = value.Replace("#", "%23");
                                    itemInfo.Href = value;
                                }
                                break;
                            case "creationdate":
                                if (!reader.IsEmptyElement)
                                {
                                    reader.Read();
                                    if (DateTime.TryParse(reader.Value, out var creationDate))
                                    {
                                        itemInfo.CreationDate = creationDate;
                                    }
                                }
                                break;
                            case "getlastmodified":
                                if (!reader.IsEmptyElement)
                                {
                                    reader.Read();
                                    if (DateTime.TryParse(reader.Value, out var lastModified))
                                    {
                                        itemInfo.LastModified = lastModified;
                                    }
                                }
                                break;
                            case "displayname":
                                if (!reader.IsEmptyElement)
                                {
                                    reader.Read();
                                    itemInfo.DisplayName = reader.Value;
                                }
                                break;
                            case "getcontentlength":
                                if (!reader.IsEmptyElement)
                                {
                                    reader.Read();
                                    if (long.TryParse(reader.Value, out long contentLength))
                                    {
                                        itemInfo.ContentLength = contentLength;
                                    }
                                }
                                break;
                            case "getcontenttype":
                                if (!reader.IsEmptyElement)
                                {
                                    reader.Read();
                                    itemInfo.ContentType = reader.Value;
                                }
                                break;
                            case "getetag":
                                if (!reader.IsEmptyElement)
                                {
                                    reader.Read();
                                    itemInfo.Etag = reader.Value;
                                }
                                break;
                            case "iscollection":
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
                                break;
                            case "resourcetype":
                                if (!reader.IsEmptyElement)
                                {
                                    reader.Read();
                                    var resourceType = reader.LocalName.ToLower();
                                    if (string.Equals(resourceType, "collection", StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        itemInfo.IsCollection = true;
                                    }
                                }
                                break;
                            case "hidden":
                            case "ishidden":
                                {
                                    itemInfo.IsHidden = true;
                                    break;
                                }
                            case "checked-in":
                            case "version-controlled-configuration":
                                {
                                    reader.Skip();
                                    break;
                                }
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
