# 史前部落（Prehistoric Tribes）重製版

THQ Wireless 2008 年 J2ME 手機遊戲《Prehistoric Tribes》以 Godot 4.7.2 .NET + C# 重做的版本：Android 與 Windows，戰役照原版規則，另有自訂遊戲、區域網路與網際網路對戰、無限大地圖的無盡模式。玩法見 [docs/PLAY.md](docs/PLAY.md)。

## 三個儲存庫

| 儲存庫 | 放在 | 內容 |
|---|---|---|
| `tribes-remake`（這個） | 根目錄 | 規則、用戶端、連線、更新、測試、建置與發行工具 |
| `tribes-content` | `data/` | 遊戲內容：規則資料表、關卡、地圖、地形、文字（`*.json`），以及圖片、音樂、字型（`data/assets/`） |
| `tribes-original` | `original/` | 原版還原：Java 還原碼與原版資源、MIDP 移植、C# 翻譯版與對照工具（見該儲存庫的 README） |

建置與遊玩只需要前兩個。`original/` 放好時，`tests/Tribes.Core.Tests`、`tests/Tribes.Graphics.Tests`、`tests/Tribes.Client.World.Tests` 會多編進 `Original/` 底下對照翻譯版、原版資源與原版畫面的測試；沒放時不編這些測試，其他照常建置。

```bash
git clone <tribes-remake 的網址> tribes
cd tribes
git clone <tribes-content 的網址> data
git clone <tribes-original 的網址> original     # 選用
```

`data/` 與 `original/` 已列在 `.gitignore`，各自在自己的儲存庫提交。

## 目錄

| 路徑 | 內容 |
|---|---|
| `src/Tribes.Core` | 遊戲規則，純 C#，不引用 Godot（[RULES.md](docs/RULES.md)、[ENDLESS.md](docs/ENDLESS.md)） |
| `src/Tribes.Graphics` | 依動畫、面向、幀與外觀算出要畫的部件（[ASSETS.md](docs/ASSETS.md)） |
| `src/Tribes.Session` | 把規則和連線接成一場對局：lockstep 模擬、玩家表、存檔與續玩（[SESSION.md](docs/SESSION.md)） |
| `src/Tribes.Net`、`src/Tribes.Net.Photon` | 建房、大廳、lockstep、中途加入與重連；區域網路與 Photon 網際網路傳輸（[NETWORK.md](docs/NETWORK.md)） |
| `src/Tribes.Update` | 遊戲內更新（[UPDATES.md](docs/UPDATES.md)） |
| `client/` | Godot 用戶端（[CLIENT.md](docs/CLIENT.md)）；`client/assets/` 是 `data/assets/` 的複本 |
| `tests/` | xUnit 測試；`Tribes.Client.*.Tests` 不在 `Tribes.sln`，要個別執行 |
| `tools/` | 打包、發行、背景執行 Godot、連線與平台檢查（[BUILD.md](docs/BUILD.md)） |
| `third_party/photon` | Photon Realtime .NET SDK 的位置；SDK 的檔案不在儲存庫裡，要自己下載放進來（見該目錄的 README） |
| `secrets/` | 權杖、簽章金鑰與 Photon AppId，不進儲存庫（[BUILD.md](docs/BUILD.md)「機密檔案」） |

## 建置與測試

需要 .NET SDK 9 與放進 `third_party/photon/` 的 Photon .NET SDK（[third_party/photon/README.md](third_party/photon/README.md)）；Godot、JDK 17、Android SDK 的位置見 [BUILD.md](docs/BUILD.md)。指令都在根目錄用 Git Bash 執行。

```bash
dotnet build Tribes.sln -c Release
dotnet test Tribes.sln
dotnet test tests/Tribes.Client.Match.Tests      # 用戶端測試（另有 Client.World、Client.Ui）
bash tools/build/package-windows.sh              # Windows 套件
bash tools/build/package-android.sh              # Android APK
```

- Godot 讀的是 `client/assets/`：`tools/godot-bg.sh`、打包腳本與 `dotnet build client/Tribes.Client.csproj` 會先從 `data/assets/` 複製有變動的檔案過去；直接用 Godot 編輯器開 `client/` 前先執行 `bash tools/sync-assets.sh`。`data/assets/` 刪掉的檔案不會跟著刪，要刪掉 `client/assets/` 再同步。
- 開發時執行 Godot 用 `bash tools/godot-bg.sh`（[BUILD.md](docs/BUILD.md)「在背景執行 Godot（開發用）」）。
- 發行新版見 [UPDATES.md](docs/UPDATES.md)。

## 其他文件

- [docs/REWRITE_PLAN.md](docs/REWRITE_PLAN.md)：重寫的目標、階段與驗證方法。
- 遊戲內容版權屬 THQ／Gear Games；本專案僅供個人研究與保存。
