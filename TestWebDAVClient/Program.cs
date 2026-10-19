using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using WebDAVClient;

namespace TestWebDAVClient
{
    class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length == 0 || args.SequenceEqual(new[] { "--help" }))
            {
                Console.WriteLine("WebDAVClient manual sample. Select a framework with -f net8.0 or -f net10.0.");
                Console.WriteLine("Use --run only after configuring the sample. Without --run no server is contacted.");
                return 0;
            }
            if (!args.SequenceEqual(new[] { "--run" }))
            {
                Console.Error.WriteLine("Unknown arguments. Use --help.");
                return 2;
            }
            await MainAsync().ConfigureAwait(false);
            return 0;
        }


        private static async Task MainAsync()
        {
            // Basic authentication required
            IClient c = new Client(new NetworkCredential { UserName = "USERNAME" , Password = "PASSWORD"});
            c.Server = "https://dav.dumptruck.goldenfrog.com/";
            c.BasePath = "/dav/";

            // List items in the root folder
            var files = await c.List();
            // Find first folder in the root folder
            var folder = files.FirstOrDefault(f => f.Href.EndsWith("/Test/"));
            // Load a specific folder
            var folderReloaded = await c.GetFolder(folder.Href);

            // List items in the folder
            var folderFiles = await c.List(folderReloaded.Href);
            // Find a file in the folder
            var folderFile = folderFiles.FirstOrDefault(f => f.IsCollection == false);

            var tempFileName = Path.GetTempFileName();

            // Download item into a temporary file
            using (var tempFile = File.OpenWrite(tempFileName))
            using (var stream = await c.Download(folderFile.Href))
                await stream.CopyToAsync(tempFile);

            // Update file back to webdav
            var tempName = Path.GetRandomFileName();
            using (var fileStream = File.OpenRead(tempFileName))
            {
                var fileUploaded = await c.Upload(folder.Href, fileStream, tempName);

                var stream = new MemoryStream(new byte[] {0xF0, 0x0B, 0xA5});
                fileUploaded = await c.UploadPartial(folder.Href, stream, tempName, 2, 6);
                if (fileUploaded)
                {
                    var parts = await c.DownloadPartial(folderFile.Href, 2, 6);
                }
            }
            
            // Create a folder
            var tempFolderName = Path.GetRandomFileName();
            var isfolderCreated = await c.CreateDir("/", tempFolderName);

            // Delete created folder
            var folderCreated = await c.GetFolder("/" + tempFolderName);
            await c.DeleteFolder(folderCreated.Href);
        }
    }
}
