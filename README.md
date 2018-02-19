GiteaPages.Net
=====
針對Gitea提供類似GitHub Pages服務

## 1.設定
appsetting.json
```javascript
{
  "defaults": [ // 預設首頁檔案名稱，有優先順序
    "index.html",
    "index.htm",
    "default.html",
    "default.htm" 
  ],
  "giteaHost": "http://git.gofa.tw", // gitea服務網址
  "cacheDir": "./Files" // 快取儲存目錄
}
```

## 2.git儲存庫設定
本服務使用master分支作為瀏覽對象，您可以透過放置`gitea.config.json`
檔案指定根目錄(如Angular 2+專案編譯後的`dist`目錄)，以及設定在找不到
指定路徑檔案時(即404錯誤)使用的檔案。
```javascript
{
    "root": "dist", // 根目錄，預設使用master本身，在Angular專案可設為`dist`目錄
    "notFound": "index.html" // 當發生404錯誤時使用的檔案，在Angular專案下可設為`index.html`達到SPA
    "scriptInjection": [ // JS腳本注入
        {
            "pattern": ".*", // 注入項目路徑匹配(正規表示式)
            "position": "Head_Start", // 注入位置(Head_Start、Head_End、Body_Start、Body_End)
            "src":"head_start.js" // 腳本路徑，如果路徑為儲存庫內部檔案，則將會把指定的JS直接作為innerHTML，反之則做為src屬性載入
        }
    ]
}
```

## 3.瀏覽
### 1.**瀏覽最新:** 
輸入網址`http://<YOUR_DOMAIN>/<USER_ID>/<REPO_NAME>`即可開啟`master`分支目前最新的內容
，也可以在上列路徑加入路徑瀏覽指定檔案(如: `http://<YOUR_DOMAIN>/<USER_ID>/<REPO_NAME>/index.html`)

### 2.**瀏覽指定Commit:**
輸入網址`http://<YOUR_DOMAIN>/<USER_ID>/<REPO_NAME>-<COMMIT_ID>`即可開啟指定Commit內容
，這個行為將會在Cookie寫入CommitId後重定向至瀏覽最新網址，透過讀取Cookie內的CommitId選擇版本。若要切換回最新版本
並清除這個行為，則可以輸入`http://<YOUR_DOMAIN>/<USER_ID>/<REPO_NAME>-last`。同項目`1`也可以在後面加入檔案
路徑。