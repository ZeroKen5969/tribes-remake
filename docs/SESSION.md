# 對局工作階段（Tribes.Session）

`src/Tribes.Session` 把規則（`Tribes.Core` 的 `GameSimulation`）和連線層（`Tribes.Net`，見 [NETWORK.md](NETWORK.md)）接成一場對局：lockstep 模擬、玩家表、主機存檔與續玩，以及用戶端每幀呼叫的對局執行器。它不依賴 Godot；用戶端的接線在 `client/src/Session`。

## 對局怎麼跑

| 型別 | 用途 |
|---|---|
| `IMatchRunner` | 對局畫面看到的對局：每幀 `Update(秒數)`，畫 `Simulation.World`，指令送 `Orders` |
| `LocalMatchRunner` | 單人（戰役、自訂、單人無盡）：直接以固定 tick 推進 `GameSimulation` |
| `NetworkMatchRunner` | 多人：包住 `HostSession` 或 `ClientSession`，由工作階段推進 `LockstepMatch` |
| `LockstepMatch` | 實作 `ILockstepSimulation`：規則＋玩家表，所有裝置得到相同的指令就得到相同的狀態 |
| `IOrderSink` | 對局畫面送出本機玩家的 `GameOrder` |
| `MatchSaveFile` | 存檔格式（單人與主機的多人存檔） |
| `RunnerSwitch` | 一個人玩的無盡世界開放給區域網路時，讓對局畫面繼續拿同一個執行器 |

### tick

- 一個 tick 120 ms（速度 25%～400% 只改實際時間的步調）。單人每幀累積時間、每幀最多跑 8 個 tick；卡住太久時丟掉落後的時間，不會暴衝追趕。多人由連線層的主機決定步調。
- `TickAlpha`（0～1）是距離上一個 tick 的比例，畫面用來平滑移動；`TickSeconds` 是目前速度下一個 tick 的實際秒數。
- 每跑完一個 tick 觸發 `TickRan`：`Result`（規則的 `TickResult`，含事件）、`LocalOrders`（與 `Result.Outcomes` 逐一對應；本機送出的指令是那個 `GameOrder`，別人的是 null）、`Changes`（這個 tick 的玩家表變化）。
- 執行器的事件都在 `Update` 裡觸發。多人時它們等連線層這一輪的更新跑完才觸發（處理常式裡可以放心呼叫 `Quit`、`Save`、`Dispose`），所以一次 `Update` 可能連續收到好幾個 `TickRan`，而 `Simulation` 已經在最新的 tick。要在每個 tick 當下讀世界（例如算雜湊），訂閱 `LockstepMatch.Stepped`；那是在連線層的步進之中，裡面不能呼叫連線的方法。

### 單人

- 新遊戲每次用新的亂數種子（無盡模式用世界種子；開發參數 `--session-seed=N` 可固定）。戰役帶入教學設定與已看過的一次性教學（`CampaignProgress.TutorialFlags`）。
- 提示會讓世界停下：對局畫面在提示、選單、說明開著時呼叫 `SetHeld(true)`。停下或暫停時 tick 照跑、期間下的指令照樣執行，只是世界不動（和原版相同）。
- 輸了以後玩家狀態變「已被消滅」、`Phase` 變 `Over`；贏了 `Phase` 也變 `Over`，世界仍可觀看。
- 無盡模式一個人玩：土地邊玩邊產生（見 [ENDLESS.md](ENDLESS.md)），自己的部落加上開局選的電腦部落數（不限數量，`LocalMatchSetup.ComputerTribes`），各有營地；新的電腦部落依密度（`NewTribes`）出現在新開出的土地；起始範圍 `StartAreaSize`。永遠不會分出勝負；自己的部落被消滅後狀態變「已被消滅」，仍可觀看。

### 多人

- 主機開始對局時送出的開局資料是 UTF-8 JSON（`MatchPlan`）：`{"mission": 8, "difficulty": 0, "seed": 1234, "endless": null, "settings": {"openingGraceFrames": 2500, "startStockPercent": 100, "wildAnimals": true}, "world": null}`；`settings` 是大廳的世界設定（開局保護的幀數、起始資源是地圖原本的百分之幾、有沒有野生動物），`null` 時對戰用預設（開局保護 5 分鐘＝2500 幀、資源 100%、有動物），無盡模式用規則預設；無盡模式 `mission` 為 -1、`endless` 為 `{"seed": 1234, "startAreaSize": 0, "computerTribes": 4, "newTribes": 2, "devAllianceHalls": false}`（`startAreaSize` 0 用規則預設；`devAllianceHalls` 只給測試腳本用：真人部落開局就有蓋好的聯盟建築與木頭、石頭）。`world` 只在接著一個已有的世界時出現：zlib 壓縮的世界存檔，base64。所有裝置用同一份資料建世界；多人對局不播開場對話，提示不讓世界停下。
- 每個 tick 先處理系統指令（加入、離開、回來、放棄），再把每個欄位的遊戲指令交給那個欄位的部落。無法解讀的內容、沒有部落或不能下指令的欄位送來的指令，在每台裝置上都同樣被忽略，不會丟出例外。
- 指令的部落由欄位決定（內容裡沒有部落），所以玩家只能指揮自己的部落。
- 輸入延遲、補送批次、追趕都由連線層處理（[NETWORK.md](NETWORK.md)「對局流程」）。

### 部落與欄位

部落的分配集中在 `ITribeControl`，預設是 `MultiTribeControl`：

- 對戰（自訂地圖，關卡 7～11）：每個欄位的持有者各玩一個自己的部落，各有一個營地（酋長小屋、倉庫、成年人）。營地在地圖放得下的範圍內盡量拉開：先照最遠優先排好，再一個個移到離其他營地最遠的空地，直到沒有營地能再拉開；地圖有空間時彼此至少 24 格（`CampPlacer.RoomyDistance`），部落多時盡量分開（最少 12 格）。每個營地在自己這一側（比其他營地都近的格子）、營地 24 格內，都要放得下 12 個 3 × 3 的標準建築位置、彼此留一格路（`CampRoom`，照建築擺放的規則：在地圖上、沒有岩石樹叢資源點或別人的建築）；做不到的部落數不能開局，所以各地圖的容量（`MatchStart.CampCapacity`）依序是 9、7、5、6、4 個部落，大廳的電腦數照這個上限。真人依欄位、電腦依它的強度。同隊的部落是盟友。
- 對戰的開局保護（世界設定，預設 5 分鐘）：保護期間電腦部落不出兵打任何部落、不追趕走進牠們領地的人，電腦的升溫從保護結束才開始算；對局中加入的部落從加入起也有同樣長的保護。電腦只防守營地 12 格內、而且比其他任何營地都近至少 4 格的格子（`CampLand.Defends`），不是原版任務裡電腦的那半張地圖；所以別人的營地與他那一側的土地永遠不在電腦的防守範圍裡，要走到電腦那一側才會被追。野生動物不會在營地周圍 12 格內出生。
- 無盡模式（`GameSimulation.StartEndless`）：每個欄位的持有者各一個部落，另外加上開房時選的電腦部落數（不限數量）；沒有隊伍，盟友要用聯盟建築邀請；永遠不會分出勝負。
- 誰在控制部落（真人、離線、已放棄、電腦）記在規則裡、跟著存檔與雜湊：離線與已放棄的部落規則也不接受指令，電腦永遠不接手真人的部落。
- 對局中加入的玩家得到一個新部落與新營地。無盡模式一定有：附近沒有空地時會往外開新土地直到放得下；對戰的自訂地圖上，新營地要放得下自己的 12 個建築位置，也不能讓任何已有的營地少於這個數；找不到這樣的地方時，加入的人沒有部落，只能觀看。
- 戰役地圖（關卡 0～6）仍是原版的兩個部落（`OriginalTribeControl`）：欄位最小的真人玩第 0 部落，第一個電腦欄位代表電腦部落，其他欄位沒有部落、只能觀看。
- 沒有欄位的電腦部落（無盡模式的、規則後來加的）也列在玩家表上（`Slot` 為 -1），一樣追蹤是否被消滅。玩家表沒有人數上限，存檔與快照都帶著整張表。

## 玩家表

每位參與者一列（`PlayerEntry`）：公開 id、名稱、欄位、部落、顏色、隊伍、是否電腦、狀態。玩家表是對局狀態的一部分：只隨 tick 的系統指令改變，和世界一起存進快照與存檔、算進狀態雜湊。

| 狀態 | 意思 | 指令 |
|---|---|---|
| `Playing` | 遊戲中 | 生效 |
| `Absent` | 離線（斷線或離開）；畫面顯示「離線」 | 不生效 |
| `Forfeited` | 已放棄，這局不能再玩 | 不生效 |
| `Out` | 部落已被消滅，這局不能再玩 | 不生效 |
| `Spectator` | 沒有欄位的觀戰者 | — |

- 離線的部落留在世界上、單位照規則自己行動，但沒有人下指令，**電腦也不會代為操作**；玩家回來後指令再次生效。
- 已放棄與已被消滅是最終狀態，之後的「回來」「離開」都不再改變它。
- 部落被消滅時每台裝置在同一個 tick 把該玩家標成 `Out`；主機另外通知連線層（`Defeat`），他之後不能再送指令。
- 本機角色 `Role`：`Player`、`Forfeited`、`Out`、`Spectator`。不在玩的人 `LocalTribe` 是 -1；`ViewTribe` 是要顯示的視野：自己原本的部落，純觀戰者看第一個真人部落。

## 離開、回來、放棄、主機離開

| 情況 | 結果 |
|---|---|
| 玩家斷線 | 連線層自動重連（每 2 秒，最多 3 次）；期間顯示「離線」，回來後補送缺的批次 |
| 玩家離開（`Quit`） | 他的部落變「離線」，其他人照常玩 |
| 玩家回來 | 以同一個玩家金鑰加入同一個房間：回到原本的欄位與部落，狀態變回「遊戲中」 |
| 玩家放棄（`Forfeit`） | 最終；之後可以繼續觀看自己原本的部落。離開再回來也只能觀戰 |
| 主機放棄或部落被消滅 | 對局照常進行；主機只是不能下指令，仍然主持與存檔 |
| 主機離開（`Quit`） | 主機先存到自動存檔。區域網路沒有主機移轉：對局結束，其他人看到「主機離開了」；主機之後可以從存檔繼續。支援移轉的傳輸（Photon、測試用 loopback）則由下一位玩家接手主持，對局不中斷 |
| 狀態不一致 | 見下方「不同步」 |
| 規則或連線出錯 | 執行器以 `StopReason.Error` 停止並寫出紀錄，畫面顯示錯誤，不會讓遊戲當掉；主機最後一次自動存檔仍可續玩 |

觀戰者以 `ClientOptions.Spectate` 加入，不占欄位、不能下指令、不能暫停。

### 暫停與速度

- 單人隨時可暫停、改速度。
- 多人：主機隨時可以；玩家只有在 `RoomSettings.AllowClientPause` 時能要求暫停／繼續，在 `AcceptSpeedRequests` 時能要求改速度（`CanPause`、`CanChangeSpeed`）。主機拒絕時收到 `Notice`（`RequestDenied`）。

### 無盡模式中途加入

主機用 `MatchSessions.SetInviteOpen(runner, true)`（多人時就是 `NetworkMatchRunner.SetInviteOpen`）讓區域網路的玩家在對局中加入（房間列表顯示「可加入」）。新玩家在某個 tick 的批次裡加入，每台裝置同時看到「加入了對局」，玩家表多一列，他在同一個 tick 得到新的部落與營地。

一個人玩的無盡世界也可以邀請：這時遊戲當場改成區域網路的主機對局（`NetworkMatchRunner.HostLocalGame`），從同一個世界、同一個 tick 接著跑，自己在第 0 欄位；開局資料帶著壓縮過的世界（`MatchPlan.World`），之後提示不再讓世界停下。對局畫面拿到的是 `RunnerSwitch`，換手時只收到 `SimulationReplaced`、`PlayersChanged`、`PhaseChanged`，不必換物件。之後的存檔是主機的多人存檔。

### 不同步

連線層每 8 個 tick 比對一次狀態雜湊（世界＋tick＋玩家表）。不一致時：

- 主機與那位玩家的紀錄都寫出 `SESSION DESYNC tick N (last matching M) player …: host 0x…, peer 0x…`；
- 主機先存到自動存檔，然後結束對局；所有人看到「各裝置的遊戲狀態不一致（第 N 個 tick：主機 0x…，玩家 0x…），對局已停止」；
- 主機可以從存檔繼續（主機的狀態是準的）。

## 存檔

### 誰存、存在哪

- 紀錄只存在主機（單人就是自己）。客戶端不能存檔（`Save` 回傳 null）。
- 用戶端寫到 `user://saves/<模式>-<欄位>.save`，旁邊是說明檔 `.json` 與縮圖 `.png`，「讀取進度」畫面列出全部。欄位為 `auto` 或 `1`～`5`。
- 自動存檔：主機每 1500 個 tick（正常速度約 3 分鐘、暫停中不計）存到 `auto`；主機離開、不同步停止時也先存。對局已分出勝負後不再自動存檔。網際網路對局移轉後，接手的人成為主機，照樣自動存到自己的裝置；之後任何時候都能以最後一位主機的存檔續玩。

### 存檔不會讓遊戲停頓

- `Save(欄位)` 在 tick 之間、主執行緒上只取狀態（單人：世界存檔；多人：主機工作階段的對局存檔），之後組檔、算雜湊、寫檔都在背景執行緒做。背景一次只寫一個，依要求的順序寫；寫完後下一次 `Update` 才發出 `Saved`（手動）、`Autosaved`（定時）或 `SaveFailed` 通知。
- `FlushSaves()` 等所有存檔寫完（讀檔前、離開對局時用）；離開與 `Dispose()` 自己會等。
- 每個檔先寫到暫存檔再搬過去取代舊檔，寫到一半當掉不會毀掉上一個存檔。
- 縮圖：`AttachPicture(欄位, 畫面)` 把畫面交給這個欄位最新的那次存檔，在背景縮成 160×90、編成 PNG、寫在存檔之後。存檔取代一個欄位時先刪掉舊縮圖；新縮圖寫不出來，這個欄位就沒有縮圖，不會顯示別的存檔的畫面；縮圖還沒寫、同一個欄位又有新的存檔時，這張縮圖就不寫了。
- 一次執行中第一次存檔特別慢（程式第一次用到才編譯，Android 上更明顯），所以對局開啟時先把整條路空跑一次（不寫檔、不通知），縮圖也先拍一張丟掉。
- 檔案位置與寫法在 `DirectorySaveStore`（用戶端是 `CatalogSaveStore`）。

| 量測（每次自動存檔、主執行緒上的工作） | 桌機 | Android 模擬器 |
|---|---|---|
| 取狀態 | 約 1.5～3 ms | 約 2 ms |
| 讀回縮圖（320×180，畫完的下一幀） | 約 1～3 ms | 約 5～12 ms |
| 組檔、寫檔、縮圖縮小編碼與寫檔 | 背景執行緒 | 背景執行緒 |

多人對局的不同步檢查每 8 個 tick 算一次狀態雜湊（世界、tick、玩家表）：桌機約 2.3～2.9 ms、Android 模擬器約 2～2.7 ms（偶爾到 10 ms），正常速度下約每秒一次，和同一幀的 tick 本身比起來不明顯。

### 格式（版本 1）

| 欄位 | 內容 |
|---|---|
| 魔術字 | `TRSV` |
| `u16` | 格式版本（2；讀得了 1） |
| `u8` | 種類：1 單人、2 主機的多人對局 |
| 區塊 | 標頭：模式、遊戲版本、資料版本、對局 id、關卡、難度、tick、tick 毫秒、存檔時間（UTC）、標題、玩家表、世界設定（格式 2 起；戰役沒有） |
| 區塊 | 內容：單人是 `GameSimulation` 的存檔；多人是主機工作階段的對局存檔（房間、名單、被踢名單、開局資料、模擬快照、已排定的指令） |
| `u32` | 前面所有位元組的 FNV-1a 雜湊 |

區塊是 `i32` 長度加資料；數字都是 little-endian，字串是 .NET `BinaryWriter` 的格式。多人存檔裡的模擬快照是 `LockstepMatch` 的快照：`TRLM`、`u16` 版本、`i64` tick、開局資料、玩家表、世界存檔。

### 更新後舊存檔照樣能用

- 每一層格式都照同一條規則：存檔檔案（`TRSV`）、對局快照（`TRLM`）、世界存檔（`GameSimulation` 的 `TRBS`）、世界快照（`WorldSnapshot`）都讀得了從 1 到這個版本的每一種格式，只拒絕比自己新的格式。
- 單人存檔：別的遊戲或資料版本做的，只要格式讀得了就照樣讀。
- 沒有上限：自訂遊戲（`MatchKind.Custom`）開局時就沒有單位與建築上限（`MissionOptions.WithoutLimits`），讀進舊版的自訂遊戲存檔時也拿掉上限（`GameSimulation.LiftLimits`）；對局與無盡世界本來就沒有上限，舊版存檔讀進來後照樣不設限；戰役存檔照原版。
- 多人存檔：主機更新後仍能續玩自己的對局（格式讀得了就行）；房間改用主機現在的版本，所以其他玩家要用和主機一樣的版本才能加入（連線的規定不變）。存檔標頭仍記著做存檔的版本，紀錄會寫出「以哪個版本續玩哪個版本的存檔」。
- 讀檔時拒絕：不是存檔（`NotASave`）、比這個版本新的格式（`NewerFormat`，畫面請玩家更新遊戲）、損壞或被改過（雜湊不符、長度不對、欄位超出範圍、多出位元組：`Damaged`）、格式讀得了但內容讀不出來的單人存檔（`OtherVersion`，畫面請玩家更新到最新版）、不是當時的主機（`NotTheHost`）、房間開不起來（連接埠被占用：`CannotHost`）。畫面顯示 `SessionText.SaveProblem` 的說明。
- 固定的舊存檔：`tests/Tribes.Session.Tests/Fixtures/` 放著各版本做的存檔（`single-player-N.save`、`hosted-match-N.save`），測試會讀每一個並接著玩 200 個 tick。只要有任何存檔或快照格式改了，舊的檔案留著不刪，用 `TRIBES_WRITE_FIXTURES=1` 跑一次 `SaveFixtureTests` 加上新版本的一組，並且繼續讀得了所有舊格式。多人的固定存檔以 `SaveFixtureTests` 裡固定的主機金鑰存檔。

### 續玩多人對局

1. 主機在「讀取進度」選多人存檔，再選在哪裡開房：「區域網路」（TCP 47801）或「網際網路」（Photon，新的房間代碼；這個建置沒有 Photon AppId 時不問，直接用區域網路）。用戶端以同一個玩家金鑰重新開房，對局停在存檔的 tick、暫停中（`Phase = WaitingForPlayers`）。
2. 畫面顯示等待面板（`ResumeWaitPanel`）：存檔裡的每位真人、是否已回來、放棄過的人「只能觀戰」，以及別人可輸入的位址。
3. 其他玩家以原本的金鑰加入房間（區域網路或網際網路分頁的房間清單，看主機選哪一邊）就回到自己的欄位與部落；放棄過的人只能觀戰。房間清單上這個房間標著「對局中」，原本的玩家看到「回到對局」，放棄過的人看到「觀戰」（[CLIENT.md](CLIENT.md)「房間清單」）；也可以「輸入位址加入」。
4. 主機按「繼續對局」（不必等所有人）；還沒回來的人是「離線」，之後仍可加入。

## 用戶端接線（client/src/Session）

- `MatchSessions`：載入時把 `MatchSimulations.Factory` 設成 `LockstepMatch`，大廳開始的對局就用真的規則。`MatchSessions.Open(MatchRequest)` 依要求建立執行器：單人新局、讀單人存檔、續玩主機存檔，或接手大廳交來的連線。讀不了的存檔丟出 `SaveFileException`。
- `CatalogSaveStore`：執行器的存檔與縮圖寫入 `user://saves`（`DirectorySaveStore`，說明檔的格式和 `SaveCatalog` 相同），寫在背景執行緒。
- `ThumbnailShooter`：拍縮圖的小視窗（和對局共用 2D 世界、不畫介面，320×180，下一幀才讀回）。
- `ResumeWaitPanel`：續玩時疊在對局畫面上的等待面板，對局開始或結束時自己消失。
- `SessionText`：通知、停止原因、存檔問題、玩家狀態的文字（`data/client/strings/ui.*.json` 的 `session.*`）。
- 對局畫面在 `_ExitTree` 呼叫執行器的 `Dispose()`：還在進行就先離開（主機會存檔），並歸還 Android 的多播鎖。

### 開發用無畫面對局

`scenes/session/session_dev.tscn` 在同一台電腦上開多個無畫面的遊戲，以區域網路對打並印出雜湊：

```
G=work/godot-4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe
"$G" --headless --path client res://scenes/session/session_dev.tscn -- --session-host=2 --session-port=47811 --session-ticks=2000 --session-name=host
"$G" --headless --path client res://scenes/session/session_dev.tscn -- --session-join=127.0.0.1:47811 --session-name=alice
"$G" --headless --path client res://scenes/session/session_dev.tscn -- --session-join=127.0.0.1:47811 --session-name=bob --session-rejoin-at=800
```

| 參數 | 說明 |
|---|---|
| `--session-host=N` | 主持，等 N 位玩家準備好後加入一個電腦部落並開始 |
| `--session-join=位址` | 加入 |
| `--session-port=P` | 主機的 TCP 埠（預設 47801） |
| `--session-ticks=T` | 玩到第 T 個 tick（預設 2000） |
| `--session-name=名稱`、`--session-seed=S`、`--session-speed=百分比`（預設 400）、`--session-mode=endless` | 名稱、對局種子、速度、模式（`endless` 是邊玩邊產生土地的無盡世界） |
| `--session-computers=N`、`--session-new-tribes=0～3`、`--session-dev-halls` | 無盡模式（主機）：開局電腦部落數（預設 2）、新部落密度（預設 2）、真人部落開局就有聯盟建築 |
| `--session-rejoin-at=T` | 玩家在第 T 個 tick 離開並以同一個金鑰回來 |
| `--session-rejoin-via=list` | 回來時不用位址，而是像玩家一樣從區域網路房間清單找到標著「回到對局」的房間（主機要加 `--session-discovery`） |
| `--session-discovery` | 主機回應區域網路的找房查詢 |
| `--session-key=GUID` | 指定玩家金鑰 |
| `--session-timeout=秒` | 超過就失敗（預設 300） |

每個實例自動下指令（也送一些無法解讀的內容），每 250 個 tick 印 `SESSION_HASH name=… tick=… hash=0x…`，到終點印 `SESSION_DONE`；失敗印 `SESSION_FAIL`，結束碼 1。

`tools/session-lan-check.sh` 一次開主機與兩個玩家（先 `dotnet build client/Tribes.Client.csproj`），逐 tick 比對三份雜湊並印出結果；環境變數 `TICKS`、`PORT`、`BOB_ARGS`（例如 `--session-rejoin-at=800`）、`HOST_ARGS`。經房間清單回來：`HOST_ARGS="--session-discovery" BOB_ARGS="--session-rejoin-at=800 --session-rejoin-via=list"`。

網際網路的對應場景是 `scenes/session/internet_dev.tscn`（經 Photon Cloud，參數 `--net-…`），一次跑整組的是 `tools/session-internet-check.sh`，含主機當機、主機中途離開、離開再從房間清單回來、無盡模式中途加入，見 NETWORK.md「網際網路傳輸」的「測試」。

存檔與雜湊的量測（對局畫面，接在 `--` 之後）：

| 參數 | 說明 |
|---|---|
| `--session-autosave-ticks=N` | 每 N 個 tick 自動存檔一次（預設 1500） |
| `--session-save-trace` | 每次存檔前 20 幀到後 150 幀的幀時間（`SESSION_FRAMES`，存檔那一幀標 `*`）與摘要；多人時每 600 幀印一次有算狀態雜湊的幀與其他幀的比較（`SESSION_HASH_FRAMES`） |

存檔的紀錄行：`SESSION_SAVE_TIMING`（主執行緒取狀態、背景組檔與寫檔的毫秒數）、`SESSION_PICTURE_TIMING`（背景縮小編碼與寫入縮圖）、`MATCH_AUTOSAVE_PICTURE`（主執行緒讀回縮圖）、`SESSION_SAVE_WARMUP`（開局時的空跑）。

## 限制

- 區域網路沒有主機移轉：主機離開對局就結束，靠主機的存檔續玩。
- 戰役地圖的多人對局只有原版的兩個部落：第二位以後的真人沒有部落、只能觀看。
- 對戰的自訂地圖放不下更多營地時，新加入的玩家沒有部落（無盡模式沒有這個限制）。
- 網際網路對局（Photon）的主機自己斷線、其他人已經由新主機繼續時，這台不能單獨推進同一場對局：用戶端呼叫 `ReportHostingLost()`，執行器存檔後以 `ConnectionLost` 停止，玩家從房間清單「回到對局」，以一般玩家回到原本的部落。
- 中途加入時，以及斷線超過主機保留的批次（約 3 分鐘）才重連時，傳完整快照，地圖越大越久。
