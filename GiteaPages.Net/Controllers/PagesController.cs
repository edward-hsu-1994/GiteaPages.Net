using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using GiteaPages.Net.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GiteaPages.Net.Controllers {
    public class PagesController : Controller {
        [Route("{repo}/{*path}")]
        public async Task<ActionResult> Get(
            [FromServices]IConfiguration configuration,
            [FromRoute]string repo,
            [FromRoute] string path = "") {
            // 取得使用者名稱
            string user = Request.Host.Value.Split('.').First();

            // 取得gitea上的master zip下載網址
            string giteaZipPath = configuration["giteaHost"];
            giteaZipPath = Path.Combine(giteaZipPath, user, repo, "archive", "master.zip")
                .Replace('\\', '/');

            // 取得master.zip下載串流
            HttpClient client = new HttpClient();

            Stream giteaZipStream = null;
            var extProvider = new FileExtensionContentTypeProvider();

            try {
                giteaZipStream = await client.GetStreamAsync(giteaZipPath);
            } catch {
                return await NotFound();
            }


            MemoryStream stream = new MemoryStream();
            bool isHtml = false;
            using (var zipStream = giteaZipStream) {
                ZipArchive arch = new ZipArchive(zipStream, ZipArchiveMode.Read, true);

                string root = arch.Entries.OrderBy(x => x.FullName).FirstOrDefault().FullName.Replace("/", "");

                ZipArchiveEntry configFile = arch.GetEntry($"{root}/gitea.config.json");
                string notFound = "404.html";

                if (configFile != null) {
                    var reader = new StreamReader(configFile.Open());
                    var config = JsonConvert.DeserializeObject<GiteaConfig>(await reader.ReadToEndAsync());
                    root = Path.Combine(root, config.Root).Replace('\\', '/');
                    notFound = config.NotFound;
                }

                ZipArchiveEntry fileEntry = null;
                if (string.IsNullOrWhiteSpace(path)) { //路徑為空值
                    var defaultFiles = configuration.GetSection("defaults");
                    if (defaultFiles == null) {
                        return await NotFound();
                    }
                    foreach (var def in configuration.GetSection("defaults").Get<string[]>()) {
                        path = def;
                        fileEntry = arch.GetEntry(Path.Combine(root, def).Replace('\\', '/'));
                        if (fileEntry != null) break;
                    }
                } else {
                    fileEntry = arch.GetEntry(Path.Combine(root, path).Replace('\\', '/'));
                }

                if (fileEntry == null) { //使用儲存庫內的404
                    fileEntry = arch.GetEntry(Path.Combine(root, notFound).Replace('\\', '/'));
                    isHtml = true;
                }

                if (fileEntry == null) { //找不到404，使用系統內建的
                    return await NotFound();
                }

                await fileEntry.Open().CopyToAsync(stream);
            }

            stream.Seek(0, SeekOrigin.Begin);

            var contentType = "application/octet-stream";
            if (isHtml) {
                contentType = extProvider.Mappings[".html"];
            } else {
                contentType = extProvider.Mappings[Path.GetExtension(path)];
            }
            return File(stream, contentType);
        }

        [NonAction]
        public async Task<ContentResult> NotFound() {
            var extProvider = new FileExtensionContentTypeProvider();

            return new ContentResult() {
                StatusCode = 404,
                ContentType = extProvider.Mappings[".html"],
                Content = await System.IO.File.ReadAllTextAsync("./ErrorPages/404.html")
            };
        }
    }
}
