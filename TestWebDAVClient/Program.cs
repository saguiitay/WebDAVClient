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
            var c = new WebDAVClient.Client(new NetworkCredential { UserName = "saguiitay@hotmail.com" , Password = "Dark2807"});
            //c.Server = "https://dhqid1025275851660103380.webdav.drivehq.com/webdav/dhqID1025275851660103380";
            c.Server = "https://webdav.4shared.com";
            c.BasePath = "/";

            var files = c.List("/").Result;
            var folder = files.FirstOrDefault(f => f.Href.EndsWith("/Test/"));
            var folderReloaded = c.Get(folder.Href).Result;

            var folderFiles = c.List(folder.Href).Result;
            var folderFile = folderFiles.FirstOrDefault(f => f.IsCollection == false);

            var x = c.Download(folderFile.Href).Result;

            var tempName = Path.GetRandomFileName();
            var fileUploaded = c.Upload(folder.Href, File.OpenRead(@"C:\Users\itay.TZUNAMI\Desktop\Untitled.png"), tempName).Result;
            
            tempName = Path.GetRandomFileName();
            var folderCreated = c.CreateDir(folder.Href, tempName).Result;

        }
    }
}
