using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GiteaPages.Net.Models;
using LiteDB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GiteaPages.Net.Controllers {
    /// <summary>
    /// Gitea Pages請求控制器
    /// </summary>
    public class PagesController : Controller {
        // lock物件清除迴圈
        public static Task LockClear;

        public LiteDatabase Database { get; private set; }

        public PagesController(LiteDatabase database) {
            this.Database = database;
            if (LockClear == null) {
                LockClear = Task.Run(() => {
                    for (; ; ) {
                        Thread.Sleep(TimeSpan.FromSeconds(30));
                        lock (Locking) {
                            var willDelete = Locking.Where(x => x.Value == 0).ToArray();

                            foreach (var commit in willDelete) {
                                DownloadLocker.Remove(commit.Key);
                                Locking.Remove(commit.Key);
                            }
                        }
                    }
                });
            }
        }

        /// <summary>
        /// 切換瀏覽版本
        /// </summary>
        /// <param name="user">使用者帳號</param>
        /// <param name="repo">儲存體名稱</param>
        /// <param name="path">檔案路徑</param>
        /// <param name="commit">取得指定commit</param>
        /// <returns>重定向</returns>
        [Route("{user}/{repo}-{commit}/{*path}")]
        [Route("{user}/{repo}-last/{*path}")]
        public async Task<ActionResult> ChangeVersion(
            [FromRoute] string user,
            [FromRoute] string repo,
            [FromRoute] string path,
            [FromRoute] string commit) {
            if (commit == "last") {
                commit = "";
            }

            Response.Cookies.Append($"{user}-{repo}", commit);

            return Redirect($"/{user}/{repo}/{path}");
        }

        /// <summary>
        /// 取得指定使用者之儲存體目標路徑檔案內容
        /// </summary>
        /// <param name="configuration">GiteaPages設定</param>
        /// <param name="user">使用者帳號</param>
        /// <param name="repo">儲存體名稱</param>
        /// <param name="path">檔案路徑</param>
        /// <returns>檔案內容</returns>
        [Route("{user}/{repo}/{*path}")]
        public async Task<ActionResult> Get(
            [FromServices]IConfiguration configuration,
            [FromRoute] string user,
            [FromRoute] string repo,
            [FromRoute] string path) {
            var repoInfos = Database.GetCollection<RepoInfo>();

            if (path == null) {
                path = string.Empty;
            }

            // 檢查是否指定版本
            Request.Cookies.TryGetValue($"{user}-{repo}", out string commit);
            if (string.IsNullOrWhiteSpace(commit)) {
                commit = null;
            }
            commit = commit?.Substring(0, 10); // 取得前十碼

            // 假設使用者沒有指定commitId，則嘗試呼叫API取得最新的commitId
            if (commit == null) {
                commit = await GetLastCommitId(user, repo);

                // 寫入資料庫
                if (commit != null) {
                    var inDbRecord = repoInfos.FindOne(x => x.User == user && x.Repo == repo);
                    if (inDbRecord == null) {
                        inDbRecord = new RepoInfo() {
                            User = user,
                            Repo = repo,
                            LastCommitId = commit
                        };
                        repoInfos.Insert(inDbRecord);
                    } else {
                        inDbRecord.LastCommitId = commit;
                        repoInfos.Update(inDbRecord);
                    }
                }
            }
            // 假設前者API請求成功則commitId則不應該為null，如為null則表示該repo不存在或無法存取
            // 將commitId設為cache內最新的項目
            if (commit == null) {
                var lastCommintInfo = repoInfos.FindOne(x => x.User == user && x.Repo == repo);

                if (lastCommintInfo == null) {
                    return await NotFound();
                } else {
                    commit = lastCommintInfo.LastCommitId;
                }
            }

            var cacheDir = GetRepoCacheDirPath(configuration["cacheDir"], user, repo, commit);

            await DownloadRepo(cacheDir, user, repo, commit);

            // zip檔案有一層根目錄
            cacheDir = Directory.GetDirectories(cacheDir).First();

            GiteaConfig config = new GiteaConfig();
            string configPath = Path.Combine(cacheDir, "gitea.config.json");
            if (System.IO.File.Exists(configPath)) {
                config = JsonConvert.DeserializeObject<GiteaConfig>(
                    System.IO.File.ReadAllText(configPath)
                );
            }



            if (!string.IsNullOrWhiteSpace(config.Root)) {
                cacheDir = Path.Combine(cacheDir, config.Root);
            }

            string fullPath = Path.Combine(cacheDir, path);

            if (string.IsNullOrWhiteSpace(path)) { //路徑為空值
                // 取得預設檔案名稱
                var defaultFiles = configuration.GetSection("defaults");
                if (defaultFiles == null) { // 沒有預設檔案
                    return await NotFound();
                }
                foreach (var def in configuration.GetSection("defaults").Get<string[]>()) {
                    fullPath = Path.Combine(cacheDir, def);
                    if (System.IO.File.Exists(fullPath)) break;
                }
            }

            // 找不到指定檔案，使用儲存庫內的404.html
            if (!System.IO.File.Exists(fullPath)) {
                fullPath = Path.Combine(cacheDir, config.NotFound);
            }

            // 儲存庫內沒有404.html
            if (!System.IO.File.Exists(fullPath)) {
                return await NotFound();
            }


            var fileStream =
                System.IO.File.Open(
                    fullPath,
                    System.IO.FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);

            var fileExtension = Path.GetExtension(fullPath);

            var extProvider = new FileExtensionContentTypeProvider();
            var contentType = "application/octet-stream";
            if (extProvider.Mappings.ContainsKey(fileExtension)) {
                contentType = extProvider.Mappings[fileExtension];
            }

            return File(fileStream, contentType);
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


        [NonAction]
        public async Task<string> GetLastCommitId(string user, string repo) {
            string url = $"http://git.gofa.tw/api/v1/repos/{user}/{repo}/branches/master";

            try {
                HttpClient client = new HttpClient();
                var responseObj = JObject.Parse(await client.GetStringAsync(url));

                return responseObj["commit"].Value<string>("id");
            } catch {
                return null;
            }
        }

        [NonAction]
        public string GetRepoCacheDirPath(string cacheDir, string user, string repo, string commitId) {
            return Path.Combine(cacheDir, user, repo, commitId);
        }

        private static Dictionary<string, object> DownloadLocker = new Dictionary<string, object>();
        private static Dictionary<string, int> Locking = new Dictionary<string, int>();

        [NonAction]
        public async Task<bool> DownloadRepo(string cacheDir, string user, string repo, string commitId) {
            //已經有快取項目
            if (Directory.Exists(cacheDir)) {
                return true;
            }

            // 檢查目前是否正在下載中了
            lock (DownloadLocker) {
                // 沒在下載中
                if (!DownloadLocker.ContainsKey(commitId)) {
                    // 建立lock物件
                    DownloadLocker[commitId] = new object();
                }
            }

            // 標註正在被locking的數量
            lock (Locking) {
                if (!Locking.ContainsKey(commitId)) {
                    Locking[commitId] = 0;
                }
                Locking[commitId]++;
            }

            lock (DownloadLocker[commitId]) {
                // 如果快取項目，其他request在lock期間已經完成下載
                if (!Directory.Exists(cacheDir)) {
                    var url = $"http://git.gofa.tw/{user}/{repo}/archive/{commitId}.zip";
                    HttpClient client = new HttpClient();

                    Stream downloadStream = null;
                    try {
                        downloadStream = client.GetStreamAsync(url).GetAwaiter().GetResult();
                    } catch { // 無法取得串流
                        return false;
                    }

                    using (ZipArchive arch = new ZipArchive(downloadStream, ZipArchiveMode.Read, true)) {
                        arch.ExtractToDirectory(cacheDir, true);
                    }
                }
            }

            // 解鎖
            Locking[commitId]--;
            return true;
        }
    }
}
