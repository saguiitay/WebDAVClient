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
        /// Download a file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="content"></param>
        /// <param name="name"></param>
        Task<bool> Upload(string remoteFilePath, Stream content, string name);

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
    }
}