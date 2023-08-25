using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WebDAVClient.Model;

namespace WebDAVClient
{
    public interface IClient : IDisposable
    {
        /// <summary>
        /// Specify the WebDAV hostname (required).
        /// </summary>
        string Server { get; set; }

        /// <summary>
        /// Specify the path of a WebDAV directory to use as 'root' (default: /)
        /// </summary>
        string BasePath { get; set; }

        /// <summary>
        /// Specify a port to use
        /// </summary>
        int? Port { get; set; }

        /// <summary>
        /// Specify the UserAgent (and UserAgent version) string to use in requests
        /// </summary>
        string UserAgent { get; set; }

        /// <summary>
        /// Specify the UserAgent (and UserAgent version) string to use in requests
        /// </summary>
        string UserAgentVersion { get; set; }

        /// <summary>
        /// Specify additional headers to be sent with every request
        /// </summary>
        ICollection<KeyValuePair<string, string>> CustomHeaders { get; set; }

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <param name="path">List only files in this path</param>
        /// <param name="depth">Recursion depth</param>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        Task<IEnumerable<Item>> List(string path = "/", int? depth = 1);

        /// <summary>
        /// Get folder information from the server.
        /// </summary>
        /// <returns>An item representing the retrieved folder</returns>
        Task<Item> GetFolder(string path = "/");

        /// <summary>
        /// Get file information from the server.
        /// </summary>
        /// <returns>An item representing the retrieved file</returns>
        Task<Item> GetFile(string path = "/");

        /// <summary>
        /// Download a file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <returns>A stream with the content of the downloaded file</returns>
        Task<Stream> Download(string remoteFilePath);

        /// <summary>
        /// Download a part of file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="startBytes">Start bytes of content</param>
        /// <param name="endBytes">End bytes of content</param>
        /// <returns>A stream with the partial content of the downloaded file</returns>
        Task<Stream> DownloadPartial(string remoteFilePath, long startBytes, long endBytes);

        /// <summary>
        /// Upload a file to the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="content"></param>
        /// <param name="name"></param>
        /// <returns>True if the file was uploaded successfully. False otherwise</returns>
        Task<bool> Upload(string remoteFilePath, Stream content, string name);

        /// <summary>
        /// Upload a part of a file to the server.
        /// </summary>
        /// <param name="remoteFilePath">Target path excluding the servername and base path</param>
        /// <param name="content">The content to upload. Must match the length of <paramref name="endBytes"/> minus <paramref name="startBytes"/></param>
        /// <param name="name">The target filename. The file must exist on the server</param>
        /// <param name="startBytes">StartByte on the target file</param>
        /// <param name="endBytes">EndByte on the target file</param>
        /// <returns>True if the file part was uploaded successfully. False otherwise</returns>
        Task<bool> UploadPartial(string remoteFilePath, Stream content, string name, long startBytes, long endBytes);

        /// <summary>
        /// Create a directory on the server
        /// </summary>
        /// <param name="remotePath">Destination path of the directory on the server</param>
        /// <param name="name">The name of the folder to create</param>
        /// <returns>True if the folder was created successfully. False otherwise</returns>
        Task<bool> CreateDir(string remotePath, string name);

        /// <summary>
        /// Deletes a folder from the server.
        /// </summary>
        Task DeleteFolder(string path = "/");

        /// <summary>
        /// Deletes a file from the server.
        /// </summary>
        Task DeleteFile(string path = "/");

        /// <summary>
        /// Move a folder on the server
        /// </summary>
        /// <param name="srcFolderPath">Source path of the folder on the server</param>
        /// <param name="dstFolderPath">Destination path of the folder on the server</param>
        /// <returns>True if the folder was moved successfully. False otherwise</returns>
        Task<bool> MoveFolder(string srcFolderPath, string dstFolderPath);

        /// <summary>
        /// Move a file on the server
        /// </summary>
        /// <param name="srcFilePath">Source path and filename of the file on the server</param>
        /// <param name="dstFilePath">Destination path and filename of the file on the server</param>
        /// <returns>True if the file was moved successfully. False otherwise</returns>
        Task<bool> MoveFile(string srcFilePath, string dstFilePath);

        /// <summary>
        /// Copies a folder on the server
        /// </summary>
        /// <param name="srcFolderPath">Source path of the folder on the server</param>
        /// <param name="dstFolderPath">Destination path of the folder on the server</param>
        /// <returns>True if the folder was copied successfully. False otherwise</returns>
        Task<bool> CopyFolder(string srcFolderPath, string dstFolderPath);

        /// <summary>
        /// Copies a file on the server
        /// </summary>
        /// <param name="srcFilePath">Source path and filename of the file on the server</param>
        /// <param name="dstFilePath">Destination path and filename of the file on the server</param>
        /// <returns>True if the file was copied successfully. False otherwise</returns>
        Task<bool> CopyFile(string srcFilePath, string dstFilePath);
    }
}