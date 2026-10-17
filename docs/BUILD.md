# 建置與打包（Godot 用戶端）

Godot 4.7.2 .NET 用戶端（`client/`）的 Windows 與 Android 建置、簽章、打包與安裝方式。遊戲內更新的發行流程見 [UPDATES.md](UPDATES.md)。

所有指令都在儲存庫根目錄用 Git Bash 執行。

- 每次匯出的 Godot 輸出寫在 `build/logs/export-<匯出設定>-<時間>.log`（每個匯出設定保留最新 20 份）。Godot 在 C# 編譯失敗時仍會產出套件，所以腳本把任何匯出錯誤或「completed with warnings」都當成失敗並刪掉產出；C# 錯誤可用 `dotnet build client/Tribes.Client.csproj` 查看。
- 匯出共用 `client/.godot`、`client/android/build` 與 `build/logs`，所以同一時間只跑一個：打包腳本先取得 `build/.export-lock`（裡面記錄持有者的 Windows 行程編號），其他匯出會等它結束（最多 1 小時）；持有者行程已不存在時自動接手。

## 事前準備

| 工具 | 位置 | 說明 |
|---|---|---|
| Godot 4.7.2 .NET 編輯器 | `work/godot-4.7.2/Godot_v4.7.2-stable_mono_win64/` | 自帶模式（資料夾內有 `_sc_`），編輯器設定與匯出範本都在其 `editor_data/` 下；腳本用 `GODOT_HOME` 可改用其他位置 |
| 匯出範本 | `editor_data/export_templates/4.7.2.stable.mono/` | 含 `android_source.zip`（Android Gradle 建置範本）與 Windows 範本 |
| .NET SDK 9 | 系統安裝 | 用戶端是 `net9.0`（Android 匯出需要） |
| JDK 17 | `tools/jdk17_unz/jdk-17.0.20.1+1` | 編輯器設定 `export/android/java_sdk_path` 指向這裡；`keytool`、`jar` 也用它 |
| Android SDK | `%LOCALAPPDATA%\Android\Sdk` | 編輯器設定 `export/android/android_sdk_path` 指向這裡；`platform-tools`（adb）與任一版 `build-tools`（apksigner） |
| 網路 | | 第一次 Android 建置時，Gradle 會下載 Gradle 8.11.1、Android Gradle Plugin 與函式庫（存到 `~/.gradle`），並把 Android 36 平台與 build-tools 36.1.0 裝進 Android SDK（SDK 授權必須已同意） |

Android 的除錯簽章用編輯器的 `editor_data/keystores/debug.keystore`（編輯器設定 `export/android/debug_keystore`）。

## 版本號

版本只在 `client/version.json` 設定：

```json
{
  "version": "1.2.0",
  "versionCode": 3,
  "contentRevision": 0
}
```

| 欄位 | 說明 |
|---|---|
| `version` | 本體版本，`X.Y.Z` 或 `X.Y.Z-標記`，要和發行標籤一致（見 UPDATES.md） |
| `versionCode` | Android 的版本碼，正整數；每次發行 APK 都要比上一版大，否則玩家的裝置拒絕安裝更新 |
| `contentRevision` | 這個建置內建的內容修訂號（UPDATES.md 的 `BuiltInContentRevision`） |

- 打包腳本會先把它寫進 `client/project.godot`（`config/version`）與 `client/export_presets.cfg`（Android 的 `version/name`、`version/code`，Windows 的檔案版本 `X.Y.Z.0`），所以不要直接改那兩處。
- `version.json` 也會打包進遊戲（`res://version.json`），遊戲以 `Tribes.Client.Platform.BuildInfo.Current` 讀取；`UpdatePlatformOptions.Create()` 用它建立 `UpdateManager` 的設定。
- 版本號沒變、但規則或資料改了的兩個建置不能一起連線對戰（規則指紋不同，見 CLIENT.md「多人房間」）。要一起測試的 Windows 與 Android 建置，請用同一份原始碼一起匯出；遊戲紀錄裡的 `NET rules fingerprint` 兩邊要相同。

## Windows

```bash
bash tools/build/package-windows.sh
```

| 產出 | 內容 |
|---|---|
| `build/release/windows/PrehistoricTribes/` | 匯出的遊戲資料夾：`PrehistoricTribes.exe`（遊戲資料嵌在執行檔內）、`data_Tribes.Client_windows_x86_64/`（.NET 組件），以及遊戲檔案清單 `install-files.txt`（遊戲內更新只換清單上的檔案） |
| `build/release/PrehistoricTribes-<版本>-windows.zip` | 上面資料夾的內容（不含外層資料夾），就是發行工具的 `--windows-zip`，也是遊戲內更新換檔用的格式 |
| `build/release/windows/check.png` | 檢查用截圖 |

- 腳本最後會把 zip 解到 `build/release/windows/check/`，以 `-- --screenshot=…` 啟動一次，存下截圖就自動結束，用來確認打包出來的遊戲能啟動。加 `--no-run` 跳過這一步。
- 執行檔圖示是 `client/src/Platform/Icons/icon.ico`。
- 玩家把 zip 解到任一個可寫入的資料夾執行 `PrehistoricTribes.exe` 即可；放在 `Program Files` 這類需要系統管理員權限的位置時，遊戲內更新會失敗（見 UPDATES.md）。

## Android

### 建置範本

Android 一律用 Godot 的 Gradle 建置（匯出設定 `gradle_build/use_gradle_build=true`）。範本裝在 `client/android/build/`，由腳本安裝，效果和編輯器的「專案 > 安裝 Android 建置範本」相同（寫入 `client/android/.build_version` 與 `build/.gdignore`）：

```bash
bash tools/android/install-build-template.sh           # 沒裝或 Godot 版本不同時才重裝，否則只更新遊戲自己的檔案
bash tools/android/install-build-template.sh --force   # 刪掉 client/android/build 重裝
```

打包腳本每次都會先執行它，平常不用自己跑。遊戲自己的 Android 程式放在 `client/android/platform/`，腳本會複製進範本：

| 檔案 | 用途 |
|---|---|
| `java/com/prehistoric/tribes/platform/UpdateInstaller.java` | 把更新 APK 交給系統安裝程式、開啟「安裝不明應用程式」設定畫面 |
| `java/com/prehistoric/tribes/platform/UpdateFileProvider.java` | 分享 APK 用的 FileProvider |
| `java/com/prehistoric/tribes/platform/MulticastLockHelper.java` | 區網搜尋房間時要持有的 Wi-Fi multicast lock |
| `java/com/prehistoric/tribes/platform/ScreenCorners.java` | 讀出螢幕四角的圓弧（Android 12 起），讓按鈕避開圓角 |
| `res/xml/tribes_update_paths.xml` | FileProvider 只分享 `files/updates/app/`（即 `user://updates/app/`） |
| `AndroidManifest.application.xml` | 插入範本 `AndroidManifest.xml` 的 `<application>` 內（FileProvider 宣告） |

- 要改 Android 程式就改 `client/android/platform/`，不要直接改 `client/android/build/`（重裝範本時會被覆蓋）。
- 腳本也會改範本的 `gradle.properties`：Gradle 堆積上限 2 GB（`GRADLE_HEAP` 可改）、不留常駐 daemon、Kotlin 在同一個行程編譯，匯出完不會留下 Java 行程。
- C# 端透過 Godot 的 `JavaClassWrapper` 與 `AndroidRuntime` 呼叫這些類別：`client/src/Platform/AndroidAppInstaller.cs`（Tribes.Update 的 `IAppInstaller`）與 `AndroidMulticast.cs`。

### 正式版簽章金鑰

只建立一次：

```bash
bash tools/android/create-release-keystore.sh
```

| 檔案 | 內容 |
|---|---|
| `secrets/android-release.keystore` | 正式版簽章金鑰（PKCS12，RSA 4096，效期 10000 天） |
| `secrets/android-signing.json` | 金鑰的 alias 與密碼（store 與 key 同一個密碼） |

- 兩個檔案的權限都只給目前的 Windows 使用者、不繼承上層權限。不要提交、不要傳出這台機器，另外離線備份一份。
- 已經存在時腳本拒絕執行。**每一版 APK 都必須用同一把金鑰簽署**，玩家才能直接覆蓋安裝更新；金鑰遺失或更換後，玩家只能解除安裝（遊戲資料會一起刪除）再裝新版。
- 密碼只經由環境變數 `GODOT_ANDROID_KEYSTORE_RELEASE_PATH`、`GODOT_ANDROID_KEYSTORE_RELEASE_USER`、`GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD` 交給 Godot，`client/export_presets.cfg` 的 `keystore/release*` 保持空白。

### 正式版 APK

```bash
bash tools/build/package-android.sh
```

產出 `build/release/PrehistoricTribes-<版本>-android.apk`（arm64-v8a，用正式版金鑰簽署），就是發行工具的 `--apk`。腳本最後以 apksigner 驗證簽章，並列出簽署者與憑證 SHA-256。

### 模擬器用 APK

```bash
bash tools/android/export-emulator.sh
```

產出 `build/client/android/PrehistoricTribes-emulator.apk`：x86_64、除錯版、以除錯金鑰簽署，接受[平台檢查](#平台檢查開發用)的參數。

### App 設定

| 項目 | 設定 |
|---|---|
| 套件名稱 | `com.prehistoric.tribes.remake` |
| App 名稱 | 系統語言為中文（`zh`、`zh_TW`、`zh_HK`）時「史前部落」，其他語言「Prehistoric Tribes」（`project.godot` 的 `config/name_localized` 與匯出設定 `package/name`） |
| 圖示 | `client/src/Platform/Icons/`：`icon-192.png`（一般圖示）、`adaptive-foreground-432.png` 與 `adaptive-background-432.png`（Android 8 以上的自適應圖示），由 `java tools/build/MakeIcons.java` 從原版圖示產生 |
| 權限 | `INTERNET`、`ACCESS_NETWORK_STATE`、`ACCESS_WIFI_STATE`、`CHANGE_WIFI_MULTICAST_STATE`、`REQUEST_INSTALL_PACKAGES` |
| 螢幕 | 隨裝置方向旋轉（尊重使用者的旋轉鎖定）、沉浸模式 |
| 最低 Android 版本 | Android 7.0（API 24），目標 API 36 |

## 安裝到裝置或模擬器

```bash
ADB="$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"
"$ADB" devices                                   # 找出裝置序號
"$ADB" -s <序號> install -r build/release/PrehistoricTribes-<版本>-android.apk
```

- 也可以把 APK 複製到手機上開啟，第一次會要求允許「安裝不明應用程式」。
- 除錯金鑰簽署的模擬器版與正式版不能互相覆蓋安裝（簽章不同）。要換版本時先 `"$ADB" -s <序號> uninstall com.prehistoric.tribes.remake`，這會刪掉遊戲資料。
- 模擬器的 x86_64 系統映像只能裝模擬器用 APK；正式版 APK 只含 ARM 程式碼。
- Godot 需要 OpenGL ES 3：模擬器要以 `-gpu host -feature GLESDynamicVersion` 啟動。
- `tools/android/smoke.sh <序號> <APK> <截圖.png> [記號]` 會安裝、啟動、等到 logcat 出現記號（例如主選單的 `TRIBES_MENU_READY`）再截圖。

## 在背景執行 Godot（開發用）

測試、截圖與檢查腳本一律用 `bash tools/godot-bg.sh <Godot 參數…>` 啟動 Godot，不直接執行編輯器的 exe。

- Godot 的視窗一律在所有視窗的最下層、不取得鍵盤焦點：載入期間先隱藏，開始執行後直接顯示在最下層，之後被叫到上面或拿到焦點時會馬上放回去並把焦點還給原本的視窗。畫面照常繪製，`--screenshot` 等截圖照常能存。
- 參數、輸出與結束碼都和直接執行 Godot 相同；啟動器被中止（例如 `timeout`）時 Godot 一起結束。
- 預設用 `work/` 裡的編輯器；`GODOT=<exe>` 可改用其他執行檔（例如打包出來的 `PrehistoricTribes.exe`）。
- 啟動器是 `tools/Tribes.GodotBackground`，第一次執行時自動建置到 `work/bin/godot-bg/`。
- `--headless` 的執行本來就沒有視窗，可以直接執行 exe。

## 平台檢查（開發用）

除錯版（模擬器用 APK、從編輯器執行）收到使用者參數 `--platform-check=<檢查>` 時，會在遊戲正常啟動的同時檢查平台功能，結果印成 `PLATFORM_CHECK …` 開頭的記錄；多項檢查用 `+` 連接。正式版忽略這個參數。程式在 `client/src/Platform/Dev/PlatformCheck.cs`（autoload `PlatformCheck`）。

| 檢查 | 內容 |
|---|---|
| `multicast` | 取得再釋放 Wi-Fi multicast lock |
| `install` | 把遊戲自己的 APK 複製到 `user://updates/app/`，照遊戲內更新的步驟交給安裝程式：還沒允許安裝時開啟權限設定畫面，允許後開啟系統安裝畫面 |
| `update` | 向更新伺服器查詢新版本（需要網路與 `secrets/github.json`） |

```bash
# 模擬器（需要 root：Godot 只接受從非公開 activity 傳入的啟動參數）
"$ADB" -s <序號> shell su 0 am start -n com.prehistoric.tribes.remake/com.godot.game.GodotApp \
  --esa command_line_params "--,--platform-check=multicast+install"
"$ADB" -s <序號> logcat -d | grep -E "PLATFORM_CHECK|TribesUpdate|TribesNet"

# 允許安裝不明應用程式後再檢查一次，就會出現系統安裝畫面
"$ADB" -s <序號> shell appops set com.prehistoric.tribes.remake REQUEST_INSTALL_PACKAGES allow

# Windows（從編輯器執行）
bash tools/godot-bg.sh --path client -- --platform-check=multicast+update
```

- `--esa` 以逗號分隔陣列元素，所以檢查之間用 `+`。
- 匯出用的範本不接受在命令列指定場景，平台檢查因此做成參數而不是另一個場景。

## 網際網路對戰的 Photon AppId

網際網路對戰（Photon Realtime，見 NETWORK.md）需要 Photon 的 AppId。它和更新用的 `readToken` 一樣，在建置時嵌進遊戲，玩家的裝置上不需要 `secrets/`：

- `secrets/photon.json` 的 `appIdRealtime` 是 Photon Dashboard 上 Realtime 應用程式的 AppId（同一個檔案的其他欄位不使用）。
- 建置 `src/Tribes.Net.Photon` 時（建置用戶端、匯出 Windows 或 Android 版都會建置它），專案檔裡的建置工作讀出 `appIdRealtime`，以遮罩方式寫進 `obj/<設定>/net8.0/EmbeddedPhotonData.g.cs`，不寫進原始碼目錄。`obj/`、`bin/` 不要提交，也不要對外分享建置中間檔。
- 沒有 `secrets/photon.json`（或建置時加上 `-p:TribesPhotonEmbedAppId=false`）時不嵌入任何 AppId：「網際網路」分頁顯示這個版本沒有網際網路對戰的設定，區域網路照常可用。另一個檔案可用 `-p:TribesPhotonSecretsFile=路徑` 指定。
- 遮罩只能讓二進位檔裡搜尋不到明文；拆開 App 的人仍然拿得到 AppId（任何用 Photon 的遊戲都一樣）。AppId 本身不能發行或改設定，被濫用時能做的是在 Photon Dashboard 換一個 AppId 並發行新版。
- 遊戲與測試都不會印出 AppId：Photon 的紀錄經過 `PhotonDiagnostics` 時會把它（與 SDK 印出的前 8 碼）換成 `***`；`PhotonSettings.ToString()` 不含它。`tests/Tribes.Net.Photon.Tests` 會檢查嵌入的值與 `secrets/photon.json` 相同，且建好的 `Tribes.Net.Photon.dll` 裡找不到它的明文。
- 換 AppId：改 `secrets/photon.json` 後重新建置並發行新版；舊版會出現「這個版本的網際網路對戰設定已失效」。

## 機密檔案

| 檔案 | 用途 |
|---|---|
| `secrets/android-release.keystore` | Android 正式版簽章金鑰 |
| `secrets/android-signing.json` | 上面金鑰的 alias 與密碼 |
| `secrets/github.json` | 更新用的 GitHub token（見 UPDATES.md） |
| `secrets/update-signing-key.pem` | 更新 manifest 簽章私鑰（見 UPDATES.md） |
| `secrets/photon.json` | 網際網路對戰的 Photon AppId（`appIdRealtime`，見上一節） |

## 已知限制

- 正式版 APK 只含 arm64-v8a。Godot 4.7 匯出 armeabi-v7a 時以 `dotnet publish -r android-arm` 建置整個方案，引用的 net8.0 程式庫（Tribes.Core、Tribes.Net、Tribes.Net.Photon、Tribes.Graphics、Tribes.Update）因此需要 NuGet 上不存在的 `Microsoft.NETCore.App.Runtime.linux-bionic-arm`，還原失敗；只支援 32 位元 ARM 的裝置無法安裝。

- Godot 4.7 的 C#/.NET Android 匯出仍標示為實驗性，匯出時會出現這則警告。
