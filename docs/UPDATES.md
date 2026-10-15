# 遊戲內更新

遊戲內的「檢查更新」按鈕會到私有儲存庫（`secrets/github.json` 的 `repo`）的 GitHub Releases 找新版本，下載後驗證簽章與雜湊，再依種類套用：

| 種類 | 內容 | 套用方式 |
|---|---|---|
| 內容包 | Godot 資源包（`.pck`），只含資料與素材 | 存到使用者資料夾，下次啟動時載入 |
| Windows 本體 | 整個安裝目錄的 zip | 遊戲結束後由更新腳本換檔並重新啟動 |
| Android 本體 | APK | 交給系統安裝程式 |

| 路徑 | 內容 |
|---|---|
| `src/Tribes.Update` | 用戶端程式庫（不依賴 Godot，net8.0） |
| `tools/Tribes.Release` | 發行工具：產生金鑰、發行、驗證、刪除 |
| `tests/Tribes.Update.Tests` | 測試 |
| `secrets/github.json` | 兩個 GitHub token（不可外流） |
| `secrets/update-signing-key.pem` | manifest 簽章私鑰（不可外流） |
| `src/Tribes.Update/UpdateKeys.json` | 遊戲信任的公鑰清單（公開，要納入版本控制） |

## 發行新版本

### 事前準備

`secrets/github.json`：

```json
{
  "repo": "<owner>/<repo>",
  "writeToken": "…可以建立 release 的 token…",
  "readToken": "…只能讀取的 token，會嵌入遊戲…"
}
```

- `writeToken` 只給發行工具用，`readToken` 會在建置時嵌入遊戲（見[安全性](#安全性)）。
- 第一次使用先產生簽章金鑰，然後重新建置遊戲，讓遊戲信任新公鑰：

```bash
dotnet run --project tools/Tribes.Release -- keygen
```

`keygen` 在 `secrets/update-signing-key.pem` 不存在時才產生新金鑰，不會覆蓋既有金鑰；接著確認其公鑰在 `UpdateKeys.json` 裡。

### 版本號與標籤

| 標籤 | 意義 |
|---|---|
| `v1.2.0` | 本體 1.2.0，內容修訂號 0 |
| `v1.2.0-r3` | 本體 1.2.0，內容修訂號 3 |
| `v0.0.1-test` | 帶標記（label）的版本，只能以預先發行版發行 |

- 版本排序：先比本體版本（`1.2.0-beta` 排在 `1.2.0` 前面），再比內容修訂號。
- **內容修訂號是跨本體版本的單一遞增計數**。每個遊戲建置都要知道自己內建的內容修訂號（`BuiltInContentRevision`）；推出新本體時，內建內容已含最新內容，就用同一個修訂號發行（例如 `v1.3.0-r5`）。
- 內容包標明「最低本體版本」（`--min-app-version`，預設等於該版本），本體版本低於它的玩家不會收到該內容包，遊戲會提示要手動更新本體。
- 標記不能是 `r` 後面只接數字（那是標籤裡的內容修訂號）。標記依語意化版本規則排序：以 `.` 分段，數字段按數值比（`beta.2` 在 `beta.10` 前面）。
- 內容修訂號最大 99999999。

### 指令

先用 `--dry-run` 檢查：會計算雜湊、寫出並簽署 manifest，但不連線 GitHub。

```bash
dotnet run --project tools/Tribes.Release -- publish --version 1.2.0 --content-rev 3 \
  --apk build/PrehistoricTribes.apk \
  --windows-zip build/PrehistoricTribes-windows.zip \
  --pck build/content.pck \
  --notes release-notes.txt --dry-run
```

拿掉 `--dry-run` 就會正式發行：建立草稿 release → 依序上傳 APK、zip、內容包（經 `uploads.github.com`），最後上傳 `manifest.json` 與 `manifest.json.sig` → 發佈。

| 參數 | 說明 |
|---|---|
| `--version X.Y.Z[-label]` | 本體版本（必填） |
| `--content-rev N` | 內容修訂號，預設 0；附內容包時至少 1 |
| `--min-app-version V` | 內容包需要的最低本體版本，預設等於 `--version` |
| `--apk` / `--windows-zip` / `--pck` | 要附上的檔案，至少一個；上傳時改用標準檔名（見下） |
| `--notes FILE` | 給玩家看的更新說明（繁體中文，UTF-8） |
| `--prerelease` | 標成預先發行版：正式版的遊戲收不到，只有本身是測試版（版本帶標記）的遊戲會收到 |
| `--title TEXT` | release 標題，預設為標籤 |
| `--out DIR` | manifest 與簽章的輸出位置，預設在系統暫存資料夾 |
| `--dry-run` | 不連線 |

標準檔名：`PrehistoricTribes-<版本>-android.apk`、`PrehistoricTribes-<版本>-windows.zip`、`content-<版本>-r<修訂號>.pck`。

- 重跑同一個指令是安全的：草稿未完成時會接著做（已上傳且雜湊相同的檔案略過、不屬於本次的檔案刪除）；已發佈且內容完全相同時什麼都不做；已發佈但檔案不同時會拒絕，請改發新的版本號或內容修訂號。
- 帶標記的版本沒加 `--prerelease` 會被拒絕，避免玩家收到測試版。遊戲也以簽章過的版本號判斷：帶標記的版本只有本身是測試版的遊戲會收到，即使有人在 GitHub 網頁上拿掉了預先發行的勾選。

### 發佈後驗證

```bash
# 以 readToken 下載並驗證這個 release（簽章、manifest、每個檔案的大小與 SHA-256），走的是遊戲用的同一套程式
dotnet run --project tools/Tribes.Release -- verify --tag v1.2.0-r3

# 模擬某個玩家按下「檢查更新」會得到什麼
dotnet run --project tools/Tribes.Release -- check --current 1.1.0 --content-rev 2 --platform windows
```

`check` 加 `--prerelease` 會把預先發行版也算進去。

### 刪除 release

```bash
dotnet run --project tools/Tribes.Release -- delete --tag v0.0.1-test --release-id 123456789 --yes
```

- 同時刪除 release 與標籤。`--release-id` 必須和該標籤的 release 相符（發行時工具會印出 id），不加 `--yes` 只顯示會刪什麼。
- 只能刪預先發行版與草稿；正式版要另加 `--allow-stable`。玩家可能已下載過，正式版請改發新版本取代。

## manifest 格式

`manifest.json`（UTF-8 JSON）：

```json
{
  "schemaVersion": 1,
  "appVersion": "1.2.0",
  "contentRevision": 3,
  "minAppVersion": "1.1.0",
  "notes": "新增：更新按鈕\n修正：存檔問題",
  "assets": [
    {
      "name": "PrehistoricTribes-1.2.0-android.apk",
      "platform": "android",
      "kind": "app",
      "size": 52428800,
      "sha256": "9f2c…（64 個小寫十六進位字元）"
    },
    {
      "name": "content-1.2.0-r3.pck",
      "platform": "any",
      "kind": "content",
      "size": 1048576,
      "sha256": "41ab…"
    }
  ]
}
```

| 欄位 | 說明 |
|---|---|
| `schemaVersion` | 格式版本，目前 `1`；遊戲遇到不認得的版本會要玩家手動更新 |
| `appVersion` / `contentRevision` | 必須和 release 標籤一致 |
| `minAppVersion` | 內容包需要的最低本體版本 |
| `notes` | 更新說明 |
| `assets[].platform` | `android`、`windows`、`any`；本體檔案不能是 `any` |
| `assets[].kind` | `app`（本體）或 `content`（內容包） |
| `assets[].size` / `sha256` | 檔案大小（位元組）與 SHA-256 |

- 檔名只能用英數字、`.`、`-`、`_`，不能以 `.` 開頭。
- 同一平台的同一種類只能有一個檔案；找檔案時先找該平台專用的，沒有才用 `any`。
- 不認得的欄位會被忽略，可以向後相容地加欄位。

`manifest.json.sig`：

```json
{
  "algorithm": "ECDSA-P256-SHA256-P1363",
  "keyId": "caeb2b808a538234",
  "signature": "…base64…"
}
```

- 簽章：以 ECDSA P-256 搭配 SHA-256，簽在 `manifest.json` 的**原始位元組**上；格式為 IEEE P1363（r 與 s 各 32 位元組串接，共 64 位元組），再轉 base64。
- `keyId`：公鑰 SubjectPublicKeyInfo（DER）的 SHA-256 前 8 個位元組，十六進位小寫。

遊戲拒絕以下情況：沒有簽章、簽章格式錯誤、`keyId` 不在信任清單、簽章不符；manifest 與 release 標籤不符，或列出的檔案不在 release 上、大小不同；下載的檔案大小或 SHA-256 與 manifest 不同。驗證簽章之前不會讀 manifest 的任何內容。

檢查更新時，被拒絕的 release 會被略過，繼續看較舊的版本（最多看最新的 3 個候選）；只有找不到其他可用更新時，才把該錯誤顯示給玩家。

## 金鑰管理與輪替

- 私鑰 `secrets/update-signing-key.pem` 是 PKCS#8 PEM，未加密；建立時權限設為只有目前的 Windows 使用者可存取、不繼承上層權限。不要提交、不要傳出這台機器，另外離線備份一份。
- `UpdateKeys.json` 列出遊戲信任的公鑰，建置時嵌入程式庫；發行工具拒絕用不在清單內的金鑰簽章。
- 發行要同時有 `writeToken` 與私鑰；只有 token 外流的人做不出遊戲會接受的 release。

### 輪替

1. 產生新金鑰並加入信任清單：`dotnet run --project tools/Tribes.Release -- keygen --key secrets/update-signing-key.next.pem`
2. 用**舊**金鑰發行一版本體更新；這版遊戲同時信任新舊兩把金鑰。
3. 等大部分玩家更新後，把新金鑰改名成 `secrets/update-signing-key.pem`（舊的留作離線備份），之後的 release 都用新金鑰簽。
4. 再下一版本體：`dotnet run --project tools/Tribes.Release -- trust remove <舊 keyId>`，之後建置的遊戲不再接受舊金鑰。`trust list` 列出目前清單。

私鑰遺失時無法替舊版遊戲簽出它會接受的 manifest：只能產生新金鑰、發行新版本體，並請玩家手動安裝一次。

## 安全性

- **嵌在 app 裡的 token 一定拿得出來。** `readToken` 在建置時以遮罩方式寫入程式，只能防止直接搜尋二進位檔找到明文，反組譯或攔截流量的人都拿得到。
- 建議 `readToken` 用 fine-grained personal access token：只限發佈用的這個儲存庫、權限只有 Contents: Read-only、設到期日。到期或撤銷後，舊版遊戲會出現「驗證失敗」，只能手動更新，所以換 token 要搭配發行新本體，並在舊 token 到期前完成。
- `writeToken` 同樣用 fine-grained token，只限這個儲存庫、Contents: Read and write、設到期日，只存在發行用的機器上。
- 即使 `readToken` 外流，別人也只能讀取 release，不能發行；遊戲只裝簽章正確的內容。
- token 只送給 `api.github.com`。下載檔案時 GitHub 會轉址到儲存主機，轉址後的請求不帶 token；只接受 HTTPS 轉址。錯誤訊息與發行工具的輸出都不含 token。
- **請求上限**：GitHub 以 token 所屬的帳號計算，每小時 5000 次 API 請求（兩個 token 屬於同一帳號時共用這個額度），而所有玩家共用同一個 `readToken`。一次「檢查更新」在沒有新版本時用 1 次請求，有新版本時約 3 次（列出 release，加上 manifest 與簽章各 1 次），下載再加 1 次；換算全部玩家合計每小時約一千多到五千次檢查。所以只在玩家按下按鈕時檢查，不要每次啟動自動檢查；玩家多了以後，應改用公開的下載點（CDN 或公開儲存庫）。超過上限時玩家會看到「次數太頻繁」。

### 建置時嵌入

- 建置 `Tribes.Update` 時會從 `secrets/github.json` 讀出 `repo` 與 `readToken`，產生 `obj/<設定>/net8.0/EmbeddedAccessData.g.cs`（token 已遮罩），不會寫進原始碼目錄。`obj/`、`bin/` 不要提交，也不要對外分享建置中間檔。
- 沒有 secrets 檔時，建置出的遊戲沒有更新功能（`EmbeddedUpdateConfig.IsConfigured` 為 false，`UpdateManager.CreateDefault` 回報「不支援自動更新」）。
- MSBuild 屬性：`TribesUpdateSecretsFile` 改用其他 secrets 檔；`TribesUpdateEmbedToken=false` 不嵌入。
- `writeToken` 永遠不會嵌入。

## 遊戲端如何套用

用戶端（Godot C#）引用 `src/Tribes.Update/Tribes.Update.csproj`，透過 `UpdateManager` 完成全部流程。每一步失敗都丟 `UpdateException`，畫面顯示其 `UserMessage`（繁體中文），`Message` 是給 log 的技術細節。

### 初始化與啟動時載入內容包

內容包必須在載入任何遊戲場景之前掛上，放在最先執行的 autoload 或開機場景：

```csharp
var options = new UpdateManagerOptions
{
    AppVersion = AppVersion.Parse("1.2.0"),          // 這個建置的本體版本
    BuiltInContentRevision = 3,                       // 這個建置內建的內容修訂號
    Platform = OS.GetName() == "Android" ? UpdatePlatform.Android : UpdatePlatform.Windows,
    DataDirectory = ProjectSettings.GlobalizePath("user://updates"),
    IncludePrereleases = false,                       // 遊戲裡由版本決定：本身是測試版才為 true
    AppInstaller = androidInstaller,                  // Android：用戶端實作的 IAppInstaller
    Windows = OS.GetName() == "Windows" ? new WindowsUpdateOptions
    {
        InstallDirectory = Path.GetDirectoryName(OS.GetExecutablePath())!,
        ExecutablePath = OS.GetExecutablePath(),
        WorkDirectory = ProjectSettings.GlobalizePath("user://updates/windows"),
    } : null,
};
UpdateManager updates = UpdateManager.CreateDefault(options);   // 整個遊戲共用一個

string? pack = updates.GetContentPackToLoad();
if (pack is not null && !ProjectSettings.LoadResourcePack(pack))
{
    updates.ContentPacks.Uninstall();   // 載入失敗就改回內建內容
}
```

- `GetContentPackToLoad` 只在內容包比內建內容新、本體版本符合最低需求、檔案大小正確時才回傳路徑；傳 `verifyHash: true` 會再比對 SHA-256（較慢）。它也會清掉舊的內容包與下載到一半的檔案。

### 「檢查更新」按鈕

```csharp
try
{
    UpdateCheckResult check = await updates.CheckAsync(token);
    if (check.Kind == UpdateKind.None)
    {
        Show(check.NewerContentNeedsNewerApp ? "有新內容，但需要先更新遊戲本體。" : "已經是最新版本。");
        return;
    }

    // 顯示 check.Tag 與 check.Notes，玩家確認後：
    string file = await updates.DownloadAsync(check, new Progress<DownloadProgress>(p => bar.Value = p.Fraction), token);
    switch (await updates.ApplyAsync(check, file, token))
    {
        case UpdateApplyOutcome.RestartToApply: Show("更新完成，重新啟動遊戲後生效。"); break;
        case UpdateApplyOutcome.QuitToFinish: GetTree().Quit(); break;                    // Windows：馬上結束
        case UpdateApplyOutcome.PermissionRequested: Show("請允許安裝後再按一次更新。"); break;
        case UpdateApplyOutcome.InstallerStarted: break;                                    // 系統安裝畫面接手
    }
}
catch (UpdateException e)
{
    Show(e.UserMessage);
}
```

- 下載可以取消（`CancellationToken`）；下載到一半的檔案（`.part`）會保留，下次從中斷處續傳。網路錯誤自動重試（預設最多嘗試 4 次，間隔 1、2、4 秒）；30 秒沒收到資料視為斷線。
- 檔案先寫成 `.part`，大小與 SHA-256 都正確才改名成正式檔名，所以正式檔名的檔案一定是驗證過的。
- 下載位置：內容包在 `user://updates/content/`，本體在 `user://updates/app/`（只保留最新一個）。

### 內容包

`ApplyAsync` 把內容包記錄到 `user://updates/content/content-state.json`，下次啟動時由上面的啟動程式載入；`ProjectSettings.LoadResourcePack` 會以包內檔案取代同路徑的內建檔案。內容包只能放資料與素材，不能帶 C# 程式碼（程式碼要發本體更新）。

### Windows 本體

1. `ApplyAsync` 把 zip 解到安裝目錄下的 `_update_staging/`（zip 可以直接放安裝目錄的內容，或包在單一最上層資料夾裡；必須含遊戲執行檔，路徑逃出資料夾的項目會被拒絕），再在 `user://updates/windows/` 寫入計畫檔與 PowerShell 腳本並啟動它，回傳 `QuitToFinish`。
2. 遊戲立刻結束。腳本等遊戲行程結束（最多 2 分鐘），把安裝目錄的舊檔案移到 `_update_backup/`、把新檔案移進來，成功就刪掉備份與暫存，重新啟動遊戲。
3. 任何一步失敗（例如檔案被防毒軟體鎖住、重試後仍失敗）就把檔案全部移回原位，啟動舊版遊戲。
4. 下次啟動時呼叫 `new WindowsAppUpdater(options.Windows).ReadLastResult()` 取得結果（`succeeded`、`rolledBack`、`rollbackFailed`、`failed`，或排了更新但腳本沒有執行的 `notRun`），顯示給玩家後呼叫 `ClearLastResult()`。

- 全部是同一個目錄內的改名，不會複製資料，也不會跨磁碟。
- 還原失敗（`rollbackFailed`）時，舊檔案可能只剩 `_update_backup/` 裡的那份。這時遊戲不會再自動更新（回報「安裝更新失敗」），要先手動把備份裡的檔案搬回安裝目錄並刪掉這個資料夾。
- 安裝目錄必須可寫入；裝在 `Program Files` 這類需要系統管理員權限的位置時，更新會失敗並還原。
- 安裝目錄裡的 `_sc_`、`._sc_`、`editor_data`（Godot 自帶模式的使用者資料）不會被動到；`WindowsUpdateOptions.PreservedEntries` 可以調整。
- 腳本以 Windows PowerShell 5.1 執行（`-ExecutionPolicy Bypass`）；若電腦以群組原則禁止執行腳本，更新不會進行，遊戲維持原版本。紀錄寫在 `user://updates/windows/swap.log`。

### Android 本體

`ApplyAsync` 把驗證過的 APK 路徑交給用戶端實作的 `IAppInstaller`（介面定義在 `src/Tribes.Update/AppInstaller.cs`）。Android 端需要一個 Godot Android 外掛：

- `AndroidManifest.xml` 宣告 `<uses-permission android:name="android.permission.REQUEST_INSTALL_PACKAGES" />`，並註冊 `androidx.core.content.FileProvider`。
- FileProvider 路徑設定：Godot 的 `user://` 在 Android 上是 `Context.getFilesDir()`，所以用 `<files-path name="updates" path="updates/app/" />`。
- `CanRequestInstall` → `PackageManager.canRequestPackageInstalls()`；`RequestInstallPermission` → 以 `Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES`（附 `package:<套件名>`）開設定畫面。
- `LaunchInstaller` → `Intent.ACTION_VIEW`，資料為 FileProvider 產生的 `content://` URI、類型 `application/vnd.android.package-archive`，加上 `FLAG_GRANT_READ_URI_PERMISSION` 與 `FLAG_ACTIVITY_NEW_TASK`。
- 新 APK 必須用同一把 Android 簽章金鑰簽署，且 `versionCode` 要比已安裝的大，否則系統拒絕安裝。

還沒有 Android 實作時可以用 `UnsupportedAppInstaller`，它會回報「不支援自動更新」。

### 錯誤種類

| `UpdateErrorKind` | 玩家看到的訊息 |
|---|---|
| `Offline` | 無法連線到更新伺服器，請確認網路連線後再試一次。 |
| `RateLimited` | 檢查更新的次數太頻繁，請稍後再試。 |
| `AuthFailed` | 更新服務驗證失敗，這個版本已無法自動更新，請下載最新版遊戲。 |
| `NotFound` | 找不到更新資料，請稍後再試。 |
| `ServerError` | 更新伺服器暫時無法使用，請稍後再試。 |
| `BadSignature` | 更新資料的簽章驗證失敗，為了安全已停止更新。 |
| `IntegrityMismatch` | 下載的檔案不完整或已損毀，請重新下載。 |
| `InvalidManifest` | 無法辨識更新資訊，請下載最新版遊戲。 |
| `DiskFull` | 裝置儲存空間不足，請清出空間後再試。 |
| `StorageError` | 無法寫入更新檔案，請確認儲存空間與權限後再試。 |
| `InstallFailed` | 安裝更新失敗，遊戲維持目前的版本。 |
| `Unsupported` | 這個版本不支援自動更新，請手動下載最新版遊戲。 |
| `Unknown` | 更新時發生未預期的錯誤，請稍後再試。 |

`RateLimited` 時 `UpdateException.RetryAfter` 是 GitHub 要求等待的時間（有提供時）。

## 測試

```bash
dotnet test tests/Tribes.Update.Tests
```

- 測試全部使用假的 HTTP 伺服器與暫存資料夾，不需要 token；Windows 換檔測試會實際執行 PowerShell 腳本（非 Windows 上略過）。
- 線上測試（只用 `readToken`、只讀取）：設 `TRIBES_LIVE_TESTS=1` 才會執行；再設 `TRIBES_LIVE_TAG=<標籤>` 會把該 release 完整驗證一次。
- 覆蓋率：`dotnet test tests/Tribes.Update.Tests --collect:"XPlat Code Coverage"`。
