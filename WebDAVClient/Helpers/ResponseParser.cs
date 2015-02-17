using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
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
        /// <param name="currentPath">The current path.</param>
        /// <param name="stream">The response text.</param>
        /// <returns>The  parsed item.</returns>
        public static Item ParseItem(string currentPath, Stream stream)
        {
            return ParseItems(currentPath, stream).FirstOrDefault();
        }

        /// <summary>
        /// Parses the disk items.
        /// </summary>
        /// <param name="currentPath">The current path.</param>
        /// <param name="stream">The response text.</param>
        /// <returns>The list of parsed items.</returns>
        public static IEnumerable<Item> ParseItems(string currentPath, Stream stream)
        {
            var items = new List<Item>();
            //var xmlBytes = Encoding.UTF8.GetBytes(responseText);
            //using (var xmlStream = new MemoryStream(xmlBytes))
            {
                using (var reader = XmlReader.Create(stream))
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
                                    reader.Read();
                                    itemInfo.Href = HttpUtility.UrlDecode(reader.Value);
                                    break;
                                case "creationdate":
                                    reader.Read();
                                    DateTime creationdate;
                                    if (DateTime.TryParse(reader.Value, out creationdate))
                                        itemInfo.CreationDate = creationdate;
                                    break;
                                case "getlastmodified":
                                    reader.Read();
                                    DateTime lastmodified;
                                    if (DateTime.TryParse(reader.Value, out lastmodified))
                                        itemInfo.LastModified = lastmodified;
                                    break;
                                case "displayname":
                                    reader.Read();
                                    itemInfo.DisplayName = HttpUtility.UrlDecode(reader.Value);
                                    break;
                                case "getcontentlength":
                                    reader.Read();
                                    int contentLength;
                                    if (int.TryParse(reader.Value, out contentLength))
                                        itemInfo.ContentLength = contentLength;
                                    break;
                                case "getcontenttype":
                                    reader.Read();
                                    itemInfo.ContentType = HttpUtility.UrlDecode(reader.Value);
                                    break;
                                case "getetag":
                                    reader.Read();
                                    itemInfo.Etag = HttpUtility.UrlDecode(reader.Value);
                                    break;
                                case "iscollection":
                                    reader.Read();
                                    bool isCollection;
                                    if (bool.TryParse(reader.Value, out isCollection))
                                        itemInfo.IsCollection = isCollection;
                                    int isCollectionInt;
                                    if (int.TryParse(reader.Value, out isCollectionInt))
                                        itemInfo.IsCollection = isCollectionInt == 1;
                                    break;
                                case "resourcetype":
                                    if (!reader.IsEmptyElement)
                                    {
                                        reader.Read();
                                        var resourceType = reader.LocalName.ToLower();
                                        if (string.Compare(resourceType, "collection", StringComparison.InvariantCultureIgnoreCase) == 0)
                                            itemInfo.IsCollection = true;
                                    }
                                    break;
                                case "hidden":
                                case "ishidden":
                                    itemInfo.IsHidden = true;
                                    break;
                                default:
                                {
                                    int a = 0;
                                    break;
                                }
                            }
                        }
                        else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName.ToLower() == "response")
                        {
                            items.Add(itemInfo);
                        }
                    }
                }
            }

            return items;
        }


    }
}
