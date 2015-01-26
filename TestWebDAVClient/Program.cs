using System.IO;
using System.Linq;
using System.Net;

namespace TestWebDAVClient
{
    class Program
    {
        static void Main(string[] args)
        {
            // Basic authentication required
            var c = new WebDAVClient.Client(new NetworkCredential { UserName = "USERNAME" , Password = "PASSWORD"});
            c.Server = "https://webdav.4shared.com";
            c.BasePath = "/";

            var files = c.List("/");
            var folder = files.FirstOrDefault(f => f.Href.EndsWith("/Test/"));
            var folderReloaded = c.Get(folder.Href);

            var folderFiles = c.List(folder.Href);
            var folderFile = folderFiles.FirstOrDefault(f => f.IsCollection == false);

            if (folderFile != null)
            {
                var x = c.Download(folderFile.Href);
            }

            var tempName = Path.GetRandomFileName();
            var fileUploaded = c.Upload(folder.Href, File.OpenRead("7_Little_Owls.jpg"), tempName);

            tempName = Path.GetRandomFileName();
            var folderCreated = c.CreateDir(folder.Href, tempName);

        }
    }
}
