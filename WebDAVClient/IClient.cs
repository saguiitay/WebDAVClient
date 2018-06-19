using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WebDAVClient.Model;

namespace WebDAVClient
{
    public interface IClient
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
        /// Specify an port (default: null = auto-detect)
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
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        Task<Item> GetFolder(string path = "/");

        /// <summary>
        /// Get file information from the server.
        /// </summary>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        Task<Item> GetFile(string path = "/");

        /// <summary>
        /// Download a file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        Task<Stream> Download(string remoteFilePath);

        /// <summary>
        /// Download a part of file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="startBytes">Start bytes of content</param>
        /// <param name="endBytes">End bytes of content</param>
        Task<Stream> DownloadPartial(string remoteFilePath, long startBytes, long endBytes);

        /// <summary>
        /// Upload a file to the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="content"></param>
        /// <param name="name"></param>
        Task<bool> Upload(string remoteFilePath, Stream content, string name);

        /// <summary>
        /// Upload a part of a file to the server.
        /// </summary>
        /// <param name="remoteFilePath">Target path excluding the servername and base path</param>
        /// <param name="content">The content to upload. Must match the length of <paramref name="endBytes"/> minus <paramref name="startBytes"/></param>
        /// <param name="name">The target filename. The file must exist on the server</param>
        /// <param name="startBytes">StartByte on the target file</param>
        /// <param name="endBytes">EndByte on the target file</param>
        Task<bool> UploadPartial(string remoteFilePath, Stream content, string name, long startBytes, long endBytes);

        /// <summary>
        /// Create a directory on the server
        /// </summary>
        /// <param name="remotePath">Destination path of the directory on the server</param>
        /// <param name="name"></param>
        Task<bool> CreateDir(string remotePath, string name);

        /// <summary>
        /// Get folder information from the server.
        /// </summary>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        Task DeleteFolder(string path = "/");

        /// <summary>
        /// Get file information from the server.
        /// </summary>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        Task DeleteFile(string path = "/");

        /// <summary>
        /// Move a folder on the server
        /// </summary>
        /// <param name="srcFolderPath">Source path of the folder on the server</param>
        /// <param name="dstFolderPath">Destination path of the folder on the server</param>
        Task<bool> MoveFolder(string srcFolderPath, string dstFolderPath);

        /// <summary>
        /// Move a file on the server
        /// </summary>
        /// <param name="srcFilePath">Source path and filename of the file on the server</param>
        /// <param name="dstFilePath">Destination path and filename of the file on the server</param>
        Task<bool> MoveFile(string srcFilePath, string dstFilePath);

        /// <summary>
        /// Copies a file on the server
        /// </summary>
        /// <param name="srcFilePath">Source path and filename of the file on the server</param>
        /// <param name="dstFilePath">Destination path and filename of the file on the server</param>
        Task<bool> CopyFile(string srcFilePath, string dstFilePath);

        /// <summary>
        /// Copies a folder on the server
        /// </summary>
        /// <param name="srcFolderPath">Source path of the folder on the server</param>
        /// <param name="dstFolderPath">Destination path of the folder on the server</param>
        Task<bool> CopyFolder(string srcFolderPath, string dstFolderPath);
    }
}