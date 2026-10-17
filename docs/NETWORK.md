# 網路連線（Tribes.Net）

`src/Tribes.Net` 是遊戲的連線層：建房、大廳、確定性 lockstep 對局、中途加入與斷線重連、主機存檔續玩。它不依賴 Godot，遊戲規則以 `ILockstepSimulation` 介面接進來；傳輸方式可替換，目前有區域網路（`LanTransport`）、網際網路（Photon Realtime：`src/Tribes.Net.Photon` 的 `PhotonTransport`）與測試用的程序內傳輸（`LoopbackTransport`），三者用同一套工作階段程式。

## 架構

```
          ┌──────────── 主機 HostSession ────────────┐
          │ 房間名單與大廳規則（HostRoom）            │
 客戶端 ──┤ 每 tick 收集指令 → 封成批次 → 轉發給所有人 ├── 客戶端
 ClientSession │ 自己也用同一批次推進模擬                │ ClientSession
          └──────────────────────────────────────────┘
```

- **星狀、主機權威**：客戶端只和主機連線；主機決定每個指令在哪一個 tick 執行，再把每個 tick 的指令批次送給所有人。
- **確定性 lockstep**：每台機器都跑完整的規則模擬，只有拿到某個 tick 的批次才推進那個 tick，所以只要規則是確定性的，所有機器的狀態一致。
- **分層**：

  | 層 | 型別 | 內容 |
  |---|---|---|
  | 傳輸 | `ITransport`、`IConnection`、`IRoomBrowser` | 可靠、有序的訊息通道；建房、連線、找房 |
  | 協定 | `Tribes.Net.Protocol`（內部） | 二進位訊息格式、快照分段、存檔格式 |
  | 工作階段 | `HostSession`、`ClientSession` | 大廳規則、lockstep、狀態雜湊比對、中途加入、存檔 |
  | 遊戲 | `ILockstepSimulation` | 規則本身，由遊戲實作 |

- **單執行緒**：兩種工作階段都要在遊戲主執行緒每一幀呼叫一次 `Update()`（Godot 放在 `_Process`）。所有事件都在 `Update()` 內（或遊戲呼叫的方法內）觸發，遊戲端不需要加鎖。LAN 的 socket 在背景執行緒收送，收到的東西排隊等 `Update()` 取出；Photon 的 SDK 只在 `Poll()` 裡處理收到的資料（它的背景執行緒只負責在遊戲暫停更新時維持連線）。
- **主機移轉**：對局中主機離開或斷線時，下一位玩家接手主持，對局不中斷（需傳輸支援，見「主機移轉」）。

## 遊戲要提供的介面

```csharp
public interface ILockstepSimulation
{
    long Tick { get; }                                  // 下一個要跑的 tick，也就是開局以來跑過的 tick 數
    void Step(IReadOnlyList<PlayerCommand> commands);   // 跑第 Tick 個 tick，之後 Tick + 1
    uint StateHash();                                   // 整個規則狀態的摘要
    byte[] SaveSnapshot();                              // 整個規則狀態（含 Tick）
    void LoadSnapshot(byte[] data);                     // 換成另一台機器存的狀態
}
```

- 工作階段用 `Func<MatchStart, ILockstepSimulation>` 建模擬：開局時用 `MatchStart` 建出起始世界；中途加入時先建一個，再 `LoadSnapshot`。
- `Step` 收到的清單在每台機器上完全相同、順序相同：先是系統指令，再是玩家指令。玩家指令的內容來自遠端玩家，規則無法使用的內容必須（在每台機器上同樣地）忽略，不能丟出例外。
- `StateHash` 與快照只能依賴規則狀態，不能依賴畫面；快照必須包含 `Tick`，以及哪些玩家暫時離開、哪些已放棄。`LoadSnapshot` 後 `Tick` 必須等於快照的 tick：主機讀存檔時不相符會丟出 `InvalidOperationException`；客戶端收到主機的快照或開局資料而模擬建不起來（工廠或 `LoadSnapshot` 丟出例外、tick 不符）時，工作階段以 `ProtocolError` 結束，例外訊息放在 `EndMessage`。
- 每個指令是 `PlayerCommand`：`Slot`（部落欄位 0–3）、`Kind`、`Payload`（遊戲自己的編碼，網路層不解讀）。

| `CommandKind` | 意思 | 規則要做的事 |
|---|---|---|
| `Game` | 玩家的操作 | 依 `Payload` 執行 |
| `PlayerJoined` | 新玩家在對局中取得這個欄位（無盡模式） | 以 `ReadJoinedPlayer()` 取得名字與顏色，建立部落；顏色決定膚色 |
| `PlayerAbsent` | 玩家斷線或離開 | 只是通知：顯示玩家不在；部落留在世界上但沒有人下指令，電腦也不代為操作 |
| `PlayerReturned` | 玩家回來了 | 顯示玩家回來；他的指令再次生效 |
| `PlayerForfeited` | 玩家放棄或被消滅，這局不能再玩 | 部落不再接受任何指令，電腦也不接手；其餘依規則處理（例如部落解散） |

**同盟**：邀請、接受、轉讓資源都是一般的 `Game` 指令，效果（禁止互相攻擊、資源轉移）由規則函式庫實作；網路層只保證它們在每台機器的同一個 tick 執行。同盟建築要列出「當下在線的玩家」時，讀 `Room.Players` 的 `Presence` 與 `IsConnected`。

## 使用方式

### 主機

```csharp
var transport = new LanTransport();                     // TCP 47801、UDP 47800
var host = new HostSession(transport, new HostOptions
{
    GameVersion = "1.0.0",
    ContentVersion = contentHash,                       // 資料內容不同的版本不能同房
    RulesFingerprint = rulesFingerprint,                // 規則或資料不同的建置不能同房（見「版本檢查」）
    PlayerId = savedPlayerId,                           // 秘密金鑰：存在裝置上、每次相同，不要給別人
    PlayerName = "Alice",
    Room = new RoomSettings { Name = "Alice 的房間", Mode = RoomMode.Endless },
}, start => new GameSimulation(start));

host.AddComputerPlayer(aiLevel: 1);                     // 用電腦補位
host.ChooseColour(5);
// 每幀：host.Update();
if (host.CanStart() == RequestResult.Accepted)
    host.Start(gameSetupBytes);                         // 地圖、種子等開局資料（最多 256 KB），所有人的模擬都收到同一份
host.SendCommand(commandBytes);                         // 主機自己的操作
byte[] save = host.SaveMatch();                         // 存到主機的檔案
host.Leave();                                           // 對局中且傳輸支援移轉：交給下一位玩家主持，對局繼續
host.Close();                                           // 或：整個房間結束
```

`Leave()` 在大廳中、或傳輸不支援主機移轉（區域網路）時等於 `Close()`。`Dispose()` 做的是 `Leave()`。離開後若要繼續玩，用同一個 `PlayerId` 建一個 `ClientSession` 連到 `host.SuccessorAddress`，以一般玩家身分回到對局。

`host.Address` 是讓別人直接連線用的位址（LAN 為 `ip:port`）。若 `LanTransport.DiscoveryError` 不是 null，表示 UDP 47800 被占用，別人找不到房間但仍可直接連線。

### 客戶端

```csharp
var browser = new LanRoomBrowser();
browser.Start();
// 每幀：browser.Poll(); 列出 browser.Rooms（RoomInfo.IsCompatible 判斷版本是否相符）

var client = new ClientSession(new LanTransport(), new ClientOptions
{
    GameVersion = "1.0.0",
    ContentVersion = contentHash,
    PlayerId = savedPlayerId,                           // 秘密金鑰：回來時靠它認人，務必保存在裝置上
    PlayerName = "Bob",
}, start => new GameSimulation(start));
client.Connect(room.Address);                           // 或使用者輸入的 "192.168.1.5:47801"
// 每幀：client.Update();
client.ChooseColour(3);
client.SetReady(true);
client.SendCommand(commandBytes);                       // 觀戰者或已放棄的玩家會回傳 false

client.BecameHost += (_, e) => host = e.Host;           // 主機離開、輪到這台主持：之後每幀改呼叫 host.Update()
```

### 續玩存檔

```csharp
SavedMatchInfo info = HostSession.ReadSaveInfo(save);   // 讀檔選單用：房名、tick、玩家
var host = HostSession.ResumeFromSave(transport, sameOptions, save, start => new GameSimulation(start));
// 對局在暫停狀態；其他玩家以原本的 PlayerId 重新連線後，主機呼叫：
host.Resume();
```

只有存檔時的主機（相同 `PlayerId`）、相同的遊戲與內容版本可以讀檔，否則丟出 `ArgumentException`；檔案損壞丟出 `ProtocolException`。續玩可以用任何傳輸：區域網路，或網際網路（`PhotonTransport` 開一個新代碼的房間）；玩家都以原本的 `PlayerId` 回來，放棄過的人成為觀戰者。續玩不會觸發 `MatchStarted`（事件還來不及訂閱），模擬直接從 `host.Simulation` 取得。`Close()` 之後仍可 `SaveMatch()`，同一個傳輸物件也可以直接拿來續玩。

### 事件

| 事件 | 主機 | 客戶端 | 何時 |
|---|---|---|---|
| `RoomChanged` | ✓ | ✓ | 名單、顏色、隊伍、準備、狀態有變 |
| `ChatReceived` | ✓ | ✓ | 有人聊天 |
| `MatchStarted` | ✓ | ✓ | 模擬建好；`FromSnapshot` 表示中途加入或重新同步 |
| `TickStepped` | ✓ | ✓ | 每跑完一個 tick |
| `PlayerPresenceChanged` | ✓ | ✓ | 跑完帶有系統指令的 tick 後，各機器在同一個 tick 觸發 |
| `DesyncDetected` | ✓ | ✓ | 狀態雜湊不一致 |
| `ControlStateChanged` | ✓ | ✓ | 暫停、繼續、改速度 |
| `SpeedRequested` | ✓ | | 玩家要求改速度 |
| `CommandRejected` | ✓ | | 丟棄了玩家的指令 |
| `WorldTooLarge` | ✓ | | 世界太大，無法傳給某位玩家（加入或回來時被拒，或要重新同步時被請出）；帶玩家、世界大小與上限 |
| `PhaseChanged` | | ✓ | `Connecting → Lobby → InGame`、`Loading`、`Reconnecting`、`Migrating`、`Ended` |
| `RequestDenied` | | ✓ | 主機拒絕了請求（例如顏色被搶先選走） |
| `Ended` | | ✓ | 結束；`Reason`、`Rejection`、`Message` 說明原因 |
| `BecameHost` | | ✓ | 主機離開，這台接手主持；事件帶新的 `HostSession`，之後改呼叫它的 `Update()` |
| `HostChanged` | | ✓ | 主機離開，已改連到接手的玩家；`HostId` 是新主機的公開 id |

### 測試用的程序內傳輸

`LoopbackNetwork` 搭配 `ManualClock`，讓主機與多個客戶端在同一個程序裡連線，時間只在呼叫 `Advance` 時前進，結果每次相同。`LoopbackTransport.Link` 設定延遲、抖動（訊息仍按順序到達）、每則訊息斷線機率與斷線察覺時間；`SimulateConnectionLoss()` 切斷連線（像網路故障），`SimulateCrash()` 讓整台消失（像當機）。預設像 Photon 一樣支援主機移轉：房間在主機離開後仍在，`TakeOverHost` 同一時間只有一人成功；`new LoopbackNetwork(clock, supportsHostMigration: false)` 則像區域網路。`LoopbackRoomBrowser` 列出同一個網路上的房間。

## 房間與大廳規則

- **欄位**：每房 4 個部落欄位，真人與電腦玩家共用。欄位滿了，第 5 位玩家以 `RejectReason.RoomFull` 被拒。
- **顏色**：系統固定 8 種，編號 0–7，同房不可重複。加入時自動拿到第一個沒人用的顏色（`PreferredColour` 有空就給它）。兩人同時選同一色時，主機先收到的成功，另一人收到 `RequestDenied`（`ColourTaken`）。對局開始後不能換色。
- **隊伍**：只用於對戰模式，編號 0–3；預設每個欄位自成一隊。
- **開局條件**：已連線的真人都按了準備（主機不用）；對戰模式至少 2 個欄位、至少 2 隊；無盡模式 1 個欄位即可。
- **觀戰**：`Spectate = true` 以觀戰者加入，不占欄位，上限 `MaxSpectators`（預設 4，最多 8）。
- **版本檢查**：協定版本、遊戲版本、內容版本任一不同就拒絕，原因分別是 `ProtocolMismatch`、`GameVersionMismatch`、`ContentVersionMismatch`，訊息寫出雙方的版本字串。
- **規則指紋**：版本號相同的兩個建置，規則或資料仍可能不同，lockstep 對局會因此不同步。主機設了 `HostOptions.RulesFingerprint` 時，指紋不同（或沒有指紋）的玩家與觀戰者在開局前就以 `RulesMismatch` 被拒。指紋放在房間廣告裡，`RoomInfo.IsCompatible(遊戲版本, 內容版本, 指紋)` 把這種房間算成版本不同；任一方沒有指紋（舊版主機）時不比對。遊戲的指紋見 [CLIENT.md](CLIENT.md)「多人房間」。
- **踢人**：`Kick` 後該玩家在這個房間（含續玩）一律以 `Banned` 被拒；對局中他的部落留在世界上、沒有人下指令。
- **聊天**：單行、最長 200 字，控制字元會被移除；每人 5 秒內最多 5 則。
- **名稱**：玩家名最長 24 字、房名最長 32 字。

## 對局流程

### tick 與指令批次

- 一個 tick 120 ms（約每秒 8.33 個），由主機的 `LockstepOptions.TickMilliseconds` 決定並套用到所有人。
- 主機每到一個 tick 就封裝該 tick 的批次（系統指令在前，玩家指令依到達順序在後），送給所有對局中的連線，然後自己跑這個 tick。主機不等任何客戶端。
- 客戶端送出指令時指定目標 tick＝「收到的最新批次 tick」＋1＋輸入延遲。主機把它排進目標 tick；已經封裝過的 tick 就排進下一個還沒封裝的 tick；太遠的未來（超過 2×最大延遲＋1）會拉近。延遲到達的指令仍在每台機器同一個 tick 執行，只是晚一點。
- 主機自己的指令排在下一個 tick 之後 `MinInputDelayTicks`（預設 1）個 tick。
- 限制：每個指令最多 1024 bytes（`MaxCommandBytes`，最多可設 64 KB），每人每個 tick 最多 32 個（`MaxCommandsPerPlayerPerTick`，最多可設 1008）；一個 tick 的玩家指令合計最多 512 KB。超過的被丟棄並回報 `TooManyCommands`。每個批次最多 64 個系統指令，多的順延到下一個 tick。

### 輸入延遲

客戶端的延遲（tick 數）＝ ⌈（RTT＋2×RTT 變異）÷ tick 長度⌉，限制在 `MinInputDelayTicks`（1）到 `MaxInputDelayTicks`（12）之間。RTT 變大時立刻跟上；變小時每 2 秒最多減 1。`ClientSession.InputDelayTicks` 與 `RoundTripTime` 可直接顯示。

### 客戶端的步調

- 客戶端照自己的時鐘每 tick 跑一個批次，沒有批次就等。
- 等待中的批次多於 `CatchUpThresholdTicks`（3）時，同一幀多跑幾個追上，每幀最多 `MaxCatchUpStepsPerUpdate`（60）個；中途加入時就是這樣快轉追上主機。
- 若連續 1 秒每一幀都至少有一個多餘的批次在排隊（例如網路一次送來好幾個），就多跑一個 tick，把這段多出來的延遲消掉。

### 狀態雜湊與不同步

- 每台機器在跑完 tick 號碼是 `HashIntervalTicks`（8）倍數的 tick 後計算 `StateHash()`；客戶端送給主機比對。
- 不一致時主機與該客戶端都觸發 `DesyncDetected`：`Tick` 是第一個不一致的比對點，`LastMatchingTick` 是最後一個一致的比對點，分歧發生在兩者之間。
- `ResyncOnDesync`（預設開）時主機立刻送一份新快照給那位玩家，把他的狀態換成主機的。

### 暫停與速度

- 主機 `Pause()` 後不再產生批次，所有客戶端跑完已收到的批次後停在同一個 tick。`Resume()` 繼續。
- 玩家可以 `RequestPause()`、`RequestResume()`；`RoomSettings.AllowClientPause = false` 時被拒。
- 速度 25%–400%，只影響實際時間的步調，不影響規則結果。玩家 `RequestSpeed()` 只是請求：主機收到 `SpeedRequested`，由主機呼叫 `SetSpeed()` 決定；`RoomSettings.AcceptSpeedRequests = true` 時直接套用。這兩項是房間設定，主機換人後照樣生效。

## 玩家狀態、中途加入與重連

主機為每個參與者記錄 `PlayerPresence`，另以 `IsConnected` 記錄目前是否連線：

| 狀態 | 意思 | 能下指令 |
|---|---|---|
| `Active` | 正在玩 | ✓ |
| `Absent` | 斷線或離開；部落沒有人下指令（電腦不代為操作），直到玩家回來 | |
| `Forfeited` | 放棄或被消滅，這局結束；可以連著觀戰 | |
| `Spectator` | 沒有欄位的觀戰者 | |

- **身分**：以客戶端自選的 `PlayerId`（GUID）辨識，同一人換連線、換 IP 都認得。`PlayerId` 是秘密金鑰，只送給主機；房間裡顯示、事件裡帶的 `PlayerInfo.Id` 是由金鑰與房間 id 雜湊（SHA-256）出的公開 id，在同一個房間（含讀檔續玩）固定不變，別人拿到它也無法冒充。同一把金鑰又連進來時，舊連線以 `ReplacedByNewConnection` 被關掉。`ClientSession.LocalPlayerId` 是自己的公開 id。
- **大廳中離開**：欄位釋出。
- **對局中離開或斷線**：狀態變 `Absent`，主機在下一個批次放入 `PlayerAbsent`；之後以同一個 `PlayerId` 回來就恢復 `Active`，批次中放入 `PlayerReturned`。
- **中途加入（無盡模式）**：房間設定 `AllowJoinInProgress` 且還有空欄位時，新玩家可以在對局中加入（主機可用 `SetJoinInProgress` 隨時開關，房間列表的 `IsOpen`、`JoinableInProgress` 同步更新）。主機在兩個 tick 之間取快照，送出 `Welcome`、快照，之後的批次照常送；新玩家在那個 tick 的批次裡收到 `PlayerJoined`。對戰模式中途只能以觀戰者加入，其他人以 `MatchInProgress` 被拒。
  - 世界解開後超過 `MaxSnapshotBytes`（256 MB）時快照送不出去：需要快照的人（新加入、觀戰、批次補不上的重連）在被算進房間之前就以 `RejectReason.WorldTooLarge` 被拒；要重新同步的玩家以 `SessionEndReason.WorldTooLarge` 被請出（狀態變 `Absent`）。兩種情況主機都觸發 `WorldTooLarge`，對局照常進行。
- **放棄**：`Forfeit()` 之後不可撤回；再次加入同一局只能觀戰。主機也可以放棄或被消滅（`Defeat(slot)` 用於規則判定某部落全滅），之後主機繼續轉發、只是不能下指令，對局照常進行。
- **自動重連**：對局中連線中斷時，客戶端每隔 `ReconnectDelay`（2 秒）重試，最多 `ReconnectAttempts`（3）次。重連時告訴主機自己的模擬停在哪個 tick；主機若還保留那之後的批次（`BatchHistoryTicks`，預設 1500 個，約 3 分鐘），只補送缺少的批次；否則送新快照。每次開局與每次讀檔續玩都有新的時間線 id，所以主機讀檔重來之後，還停在舊進度的客戶端一定收到新快照，不會接上另一條時間線的批次。
- **主機離開**：見下一節「主機移轉」。不支援移轉的傳輸（區域網路）上，主機離開就結束這次連線；無盡模式由主機日後讀存檔續玩，其他人再重新加入。
- **存檔內容**：房間設定與名單、被踢名單、開局資料、目前 tick、速度、模擬快照，以及已排定但還沒執行的指令。續玩時除主機外的真人都是 `Absent`，續玩後的第一個批次就放入他們的 `PlayerAbsent`。

## 主機移轉

對局中主機離開（`Leave()`、關掉遊戲）或整台斷線時，對局不會停：另一位玩家接手主持，其他人改連到他，從大家都有的進度繼續。這需要傳輸支援（`ITransport.SupportsHostMigration`）：Photon 與測試用的程序內傳輸支援（Photon 的做法見「網際網路傳輸」）；區域網路不支援，見「區域網路傳輸」。

- **接手順位**：主機隨每次房間更新送出順位表（RoomExtras）：已連線、能接手的玩家依欄位由小到大，接著是觀戰者（依加入順序），每人附上移轉位址（`ITransport.MigrationAddress`）。
- **觸發**：
  - 主機 `Leave()`：送出 `HostLeaving`（指名順位第一的人），然後離開房間。
  - 主機整台斷線：傳輸回報 `DisconnectReason.HostLeft`（房間還在，只有主機不見了）。
  - 自己的連線斷掉（`ConnectionLost`）不算：照一般重連連回原主機；重連時發現主機已不在房間裡，才改為移轉。
- **選出接手者**：房間已記錄新的主機（`ITransport.RoomHostAddress`，也就是搶到 `TakeOverHost` 的人）時，直接連過去。還沒有時，每台照同一份順位表挑第一位還在房間裡的人（`ITransport.IsPeerPresent`），被指名的人優先，所以大家選到同一位：輪到自己就接手，否則連過去（`ClientPhase.Migrating`）。對方還沒準備好就每 200 ms 重試；重試前先看房間記錄的主機，順位表過時（例如還沒收到最新的房間更新）也會連到真正的新主機。`MigrationTimeout`（10 秒）內連不上就跳過他、換下一位。還在載入快照的玩家沒有完整的模擬，不能接手；順位裡沒有人能接手時，工作階段以 `HostClosed` 結束。
- **接手**：接手者先跑完已收到的批次，然後：
  - 用帳（Ledger）重建房間：欄位與狀態以所有模擬都跑過的批次為準，名稱、顏色等取自原主機最後一次的房間更新。原主機排定但還沒放進批次的變更（例如剛加入、還沒送出 `PlayerJoined` 的玩家）不算，那位玩家重新加入即可。
  - 原主機的欄位設為 `Absent`（放入 `PlayerAbsent`）；玩家已要求、但還沒生效的放棄照樣生效。
  - 其他玩家在 `MigrationGracePeriod`（5 秒）內連回來不會被標成離開，逾時才放入 `PlayerAbsent`。
  - 收到過的批次成為歷史、自己算過的狀態雜湊成為比對基準，所以連回來的人可以只補缺的批次，雜湊比對也不中斷。
  - 以傳輸的 `TakeOverHost` 成為房間的主機；同時有兩人搶時只有一人成功，另一人改連成功的那位。
  - 觸發 `ClientSession.BecameHost`，帶著新的 `HostSession`（同一個模擬物件）；原本的 `ClientSession` 以 `SessionEndReason.BecameHost` 結束。其他人收到 `HostChanged`。
- **從哪裡繼續**：從接手者收到的最後一個批次之後繼續，並換一個新的時間線 id，同時記得舊時間線到哪一個 tick 為止和新的相同。比接手者少收的人補收缺的批次；多跑了接手者沒有的批次的人收到新快照（`MatchStarted.FromSnapshot`），回到新的時間線。
- **指令不遺失、不重複**：每個遊戲指令帶有玩家自己的編號（1、2、3…）。主機依編號順序把同一位玩家的指令排進批次，並丟掉已經有的編號。客戶端保留最近送出的 1024 個指令，連上新主機（以及一般重連、或從快照回到新時間線）後，把帳上還沒跑過的指令再送一次。所以送給原主機但還沒排進批次的指令，以及只在被捨棄的批次裡跑過的指令，都會再跑，而且只跑一次。
- **原主機回來**：原主機用同一個 `PlayerId` 建 `ClientSession`，連到 `SuccessorAddress`（或從房間列表找到房間），以一般玩家回到原本的欄位（`PlayerReturned`）。
- **可以移轉多次**：接手的人之後離開或斷線，再由下一位接手。
- **限制**：誰還在房間裡、誰是主機，都由傳輸（Photon 的伺服器）判斷，而 `TakeOverHost` 同一時間只讓一人成功，所以不會出現兩個主機。若接手順位第一的人裝置還在房間裡、遊戲卻停住不動，其他人要等 `MigrationTimeout` 後才換下一位。

## 訊息格式

### 基本編碼

| 型別 | 編碼 |
|---|---|
| `u8`、`bool` | 1 byte；bool 只能是 0 或 1 |
| `u16`、`u32`、`i64` | 固定長度，little-endian |
| `uvarint` | LEB128 無號變長整數，最多 10 bytes |
| `varint` | zigzag 後以 LEB128 編碼的有號整數 |
| `string` | `uvarint` 長度＋UTF-8（最多 256 bytes，聊天 1024） |
| `bytes` | `uvarint` 長度＋原始資料 |
| `guid` | 16 bytes（.NET `Guid.TryWriteBytes` 的順序） |

每則工作階段訊息＝`u8` 訊息類型＋內容。讀取時任何越界、超過上限、未定義的列舉值或多餘的尾端位元組都視為協定錯誤：主機關閉該連線，客戶端結束工作階段（`ProtocolError`）。

每則訊息最多 1 MB（`NetProtocol.MaxMessageBytes`），每種傳輸都必須能送到這個大小。工作階段保證不超過：開局資料最多 256 KB（`NetProtocol.MaxGameSetupBytes`，`Start` 超過時丟出 `ArgumentException`）、快照分段最多 512 KB、一個批次的玩家指令合計最多 512 KB 加上最多 64 個系統指令。

### 共用結構

| 結構 | 欄位（依序） |
|---|---|
| PlayerInfo | `guid` 公開 id、`string` 名稱、`varint` 欄位（−1 觀戰）、`u8` 種類（0 真人、1 電腦）、`varint` 顏色（−1 無）、`varint` 隊伍、`bool` 準備、`u8` 狀態、`bool` 連線中、`bool` 主機、`varint` AI 等級 |
| RoomSettings | `string` 房名、`u8` 模式（0 對戰、1 無盡）、`varint` 欄位數、`bool` 允許中途加入、`bool` 允許觀戰、`varint` 觀戰上限、`bool` 玩家可暫停、`bool` 速度請求直接套用 |
| RoomState | `guid` 房間 id、RoomSettings、`u8` 階段（0 大廳、1 對局、2 關閉）、`varint` 版本號、`uvarint` 人數＋PlayerInfo… |
| MatchStart | `guid` 對局 id、`u8` 模式、`varint` tick 毫秒、`varint` 雜湊間隔、`uvarint` 人數＋PlayerInfo…、`bytes` 開局資料 |
| PlayerCommand | `uvarint` 欄位、`u8` 種類、`varint` 指令編號（遊戲指令；系統指令為 0）、`bytes` 內容（`PlayerJoined` 的內容是 PlayerInfo） |
| TickBatch | `varint` tick、`uvarint` 指令數＋PlayerCommand… |
| RoomExtras | `uvarint` 被踢人數＋`guid`…、`uvarint` 接手順位人數＋（`guid` 公開 id、`string` 移轉位址）… |
| Ledger | `uvarint` 人數＋PlayerInfo…（欄位持有者與狀態，照已跑過的批次）、4 個 `varint`（每個欄位已跑過的最大指令編號） |

### 客戶端 → 主機

| 類型 | 名稱 | 內容 |
|---|---|---|
| 1 | Hello | `u16` 協定版本、`string` 遊戲版本、`string` 內容版本、`guid` 玩家金鑰（`PlayerId`）、`string` 名稱、`bool` 觀戰、`varint` 想要的顏色（−1）、`guid` 時間線 id（重連用，否則全 0）、`varint` 模擬目前的 tick（重連用，否則 −1）、`string` 移轉位址（這台接手主持時別人連它的位址；不支援時為空）、`string` 規則指紋（沒有時為空） |
| 2 | Chat | `string` 內容 |
| 3 | ChooseColour | `varint` 顏色 |
| 4 | ChooseTeam | `varint` 隊伍 |
| 5 | SetReady | `bool` |
| 6 | Leave | — |
| 7 | Forfeit | — |
| 8 | Command | `varint` 目標 tick、`varint` 指令編號（這位玩家的第幾個指令，從 1 起）、`bytes` 指令 |
| 9 | StateHash | `varint` tick、`u32` 雜湊 |
| 10 | ControlRequest | `u8`（0 暫停、1 繼續、2 速度）、`varint` 速度 |

連線後的第一則訊息必須是 Hello，且要在 10 秒內送到（`HelloTimeout`）。協定版本放在最前面，版本不同的客戶端即使後面格式不同也會收到 `ProtocolMismatch`。

### 主機 → 客戶端

| 類型 | 名稱 | 內容 |
|---|---|---|
| 32 | Welcome | RoomState、RoomExtras、`varint` 補送起點 tick（−1 表示不補送：大廳中，或接著送快照）、`guid` 主機的時間線 id（大廳中全 0）、`varint` 這位玩家欄位已有的最大指令編號 |
| 33 | Reject | `u8` RejectReason、`string` 原因 |
| 34 | RoomUpdate | RoomState、RoomExtras |
| 35 | ChatRelay | `guid` 發言者、`string` 名稱、`string` 內容 |
| 36 | RequestDenied | `u8` RequestKind、`u8` RequestResult、`string` 說明 |
| 37 | MatchBegin | MatchStart、`varint` 速度、`guid` 時間線 id |
| 38 | SnapshotBegin | MatchStart、`varint` tick、`varint` 原始長度、`u32` 校驗碼、`bool` 已壓縮、`varint` 分段數、`varint` 速度、`bool` 暫停中、`guid` 時間線 id、Ledger（快照那個 tick 的帳） |
| 39 | SnapshotChunk | `varint` 序號、`bytes` 資料 |
| 40 | Batch | TickBatch |
| 41 | ControlState | `bool` 暫停中、`varint` 速度 |
| 42 | DesyncNotice | `varint` 不一致的 tick、`varint` 最後一致的 tick、`guid` 玩家、`u32` 主機雜湊、`u32` 玩家雜湊 |
| 43 | SessionEnd | `u8` SessionEndReason、`string` 原因 |
| 44 | HostLeaving | `guid` 接手者的公開 id；主機在對局中離開前送出 |

### 快照傳送

快照 256 bytes 以上時以 zlib（RFC 1950，最快等級）壓縮，壓縮後比較小才採用。校驗碼是原始資料的 FNV-1a 32 位元雜湊。資料切成最多 32 KB 一段（`SnapshotChunkBytes`），按序號依序送出；之後緊接著送快照 tick 起的批次。快照解開後最多 256 MB（`NetProtocol.MaxSnapshotBytes`，`LockstepOptions.MaxSnapshotBytes` 預設值）：主機不送更大的快照，而是告訴玩家世界太大（見「中途加入」）；客戶端收到更大的快照開頭就以 `SessionEndReason.WorldTooLarge` 結束。客戶端把收到的各段直接解壓進一份剛好大小的陣列，大世界在記憶體裡只多一份。收到多少可以從 `ClientSession.SnapshotDownload` 看（已收段數、總段數、解開後的大小）。

### 存檔格式

`TRBS`、`u16` 格式版本（1）、`string` 遊戲版本、`string` 內容版本、RoomState、`uvarint` 被踢人數＋`guid`…、MatchStart、`varint` tick、`varint` 速度、`varint` 快照原始長度、`bytes` zlib 壓縮的快照、尚未送出的系統指令（同 TickBatch 的指令清單）、`uvarint` 已排定指令數＋（`varint` tick、PlayerCommand）…、Ledger（存檔 tick 的帳）。

## 區域網路傳輸（LanTransport）

### 連接埠

| 用途 | 協定 | 預設埠 | 設定 |
|---|---|---|---|
| 對局連線 | TCP | 47801 | `LanTransportOptions.Port`（0＝任選，實際埠看 `LocalPort`） |
| 找房 | UDP | 47800 | `LanTransportOptions.DiscoveryPort`、`LanDiscoveryOptions.Port` |

Windows 第一次當主機時防火牆可能詢問是否允許；要讓其他電腦連進來必須允許。同一台電腦上可以同時有多個主機：TCP 各用不同埠，UDP 47800 以位址重用共享。

### TCP 訊框

每個訊框＝`i32` 長度（little-endian，算種類與內容）＋`u8` 種類＋內容，單一訊框最大 1 MB（`MaxMessageBytes`，不能小於 `NetProtocol.MaxMessageBytes`）。

| 種類 | 名稱 | 內容 |
|---|---|---|
| 1 | Handshake | `TRBN`＋`u16` 協定版本；客戶端的第一個訊框。版本只供參考：訊框格式固定不變，傳輸層不比對版本，由工作階段的 Hello 判斷，版本不同的玩家會收到 `ProtocolMismatch` |
| 2 | HandshakeAck | — |
| 3 | Data | 一則工作階段訊息 |
| 4 | Ping | `i64` 送出時間戳；握手完成後才能送 |
| 5 | Pong | 原封不動送回 Ping 的時間戳，用來算 RTT |
| 6 | Close | — 正常關閉；送完排隊中的訊框後才關 |

- 握手要在 5 秒內完成（`ConnectTimeout`），內容不對就直接關閉，不會出現在 `Connections`。
- 每 500 ms 互送 Ping（`PingInterval`）；10 秒沒收到任何訊框就以 `Timeout` 斷線（`Timeout`）。
- RTT 以 TCP 相同的平滑方式計算（新樣本占 1/8，變異占 1/4）；負值或超過 1 分鐘的樣本視為偽造或錯誤而忽略。
- 直接連線的位址格式：`192.168.1.5:47801`、`192.168.1.5`（用預設埠）、`[::1]:47801`、`主機名稱:47801`。

### 找房

- `LanRoomBrowser` 每秒（`QueryInterval`）廣播一個 14 bytes 的查詢：`TRBQ`、`u16` 協定版本、`i64` 時間戳；送往 255.255.255.255、每張網卡子網路的廣播位址，以及 `ExtraTargets`。
- 主機用單播回覆：`TRBR`、`u16` 協定版本、`i64` 查詢的時間戳（用來算 ping）、`u16` TCP 埠、`guid` 房間 id、`string` 房名、`string` 主機名稱、`u8` 模式、`varint` 已占欄位、`varint` 欄位數、`varint` 觀戰人數、`string` 遊戲版本、`string` 內容版本、`bool` 對局中、`bool` 可加入、`bool` 可中途加入。這個格式在之後的協定版本也維持不變，舊版遊戲也能看到新版房間（並以 `IsCompatible` 判斷不能加入）。
- 之後可接延伸欄位：`u8` 延伸版本（1）、`bool` 還有觀戰名額、`uvarint` 人數＋（`guid` 公開 id、`u8` 狀態）…，列出占欄位的真人（`RoomAdvertisement.Members`、`SpectatorsWelcome`）。延伸版本 2 再接 `string` 規則指紋（`RulesFingerprint`，空字串表示沒有）。讀的一方只讀自己認得的版本的欄位、忽略後面的內容；舊版主機沒有延伸欄位時這兩項為 null。`RoomInfo.PresenceOf(金鑰)` 以這台裝置的玩家金鑰找出自己在這個房間的狀態（回來的玩家、已放棄的玩家），房間清單據此顯示「回到對局」或「觀戰」。
- 房間的位址是「回覆的來源 IP：回覆裡的 TCP 埠」。4 秒（`RoomTimeout`）沒收到回覆的房間會從列表移除。
- 找房只支援 IPv4 廣播；IPv6 只能直接連線。


## 網際網路傳輸（PhotonTransport）

網際網路對戰走 Photon Realtime（Exit Games 的雲端轉送服務）。`src/Tribes.Net.Photon` 以 `PhotonTransport : ITransport` 與 `PhotonRoomBrowser : IRoomBrowser` 實作上面的傳輸介面，`HostSession`、`ClientSession` 不必知道底下是 Photon。只有這個專案引用 Photon SDK（`third_party/photon`，Photon .NET SDK 5.1.20，見該目錄的 README）；`Tribes.Net` 本身不依賴 Photon。

```csharp
PhotonSettings settings = PhotonSettings.FromBuild()!;       // 建置時嵌入的 AppId；沒有時為 null
var host = new HostSession(new PhotonTransport(settings.WithRegion("jp")), hostOptions, factory);
// host.Address 在 Photon 建好房間後變成 "jp/K7QM2P"，把它（或代碼 K7QM2P）告訴朋友

var client = new ClientSession(new PhotonTransport(settings), clientOptions, factory);
client.Connect("jp/K7QM2P");                                  // 或 "K7QM2P"：連到的地區裡的房間

var browser = new PhotonRoomBrowser(settings);                // 一個地區的房間清單
browser.Start();                                              // 之後每幀 Poll()
```

- **AppId**：`PhotonSettings.FromBuild()` 取得建置時嵌入的 AppId（來源與嵌入方式見 BUILD.md「網際網路對戰的 Photon AppId」）；工具與測試可用 `PhotonSettings.ForAppId(...)`。AppId 不公開成屬性，`ToString()` 也不含它。
- **AppVersion**：Photon 只讓相同 AppVersion 的玩家看到彼此的房間。預設 `tribes-net-<協定版本>`，跟著網路協定而不是遊戲版本，所以其他遊戲版本的房間仍會列出（標示「版本不同」）。
- **一個傳輸一條 Photon 連線**：每個 `PhotonTransport`、`PhotonRoomBrowser` 連上 Photon 時各算一個同時連線數（CCU）。客戶端離開房間就斷開 Photon，主機 `Shutdown` 也一樣。

### 位址

| 位址 | 意思 |
|---|---|
| `region/CODE`（例如 `jp/K7QM2P`） | 房間：建立它的地區與 6 字元的房間代碼（字元集 `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`，沒有 0/O、1/I）。`RoomInfo.Address`、`HostSession.Address` 都是這個形式 |
| `CODE` | 玩家只打代碼時：已連上的地區，否則 `PhotonSettings.Region`（沒有時為最近的地區）裡的房間 |
| `actor:N` | 房間裡的某一位（Photon 的 actor 號碼），用於主機移轉：`MigrationAddress`、`RoomHostAddress`、`Connect("actor:N")` |

`PhotonAddress.TryParseRoom` 接受大小寫與空白（`JP / k7qm2p`），代碼重複時建房會自動換一個。

### 房間

- 主機 `StartHost` 時連上 Photon（地區見下方）並以新代碼 `OpCreateRoom`（屬性 `h`、`a` 一起建立）：`MaxPlayers` 16（4 個欄位、8 位觀戰者，再加上重連者舊連線還沒逾時的餘裕）、`IsOpen`、`IsVisible` 永遠為 true、`PlayerTtl` 與 `EmptyRoomTtl` 為 0（斷線的人立刻離開房間，最後一人離開房間就消失）、`CleanupCacheOnLeave`。誰能進來、能不能中途加入，由工作階段決定（`Hello` 之後的 `Reject`），不靠 Photon 的房間設定。
- 房間屬性：

  | 屬性 | 型別 | 內容 |
  |---|---|---|
  | `h` | int | 主持工作階段的 actor。建房時就寫進去（建房者是房間的第一位 actor，1），所以接手時一定有值可以比較；移轉時以比較後交換改成新主機 |
  | `a` | byte[] | 房間廣告：協定版本（u16），接著與區域網路找房相同的廣告格式（名稱、模式、人數、版本、規則指紋、對局中、可加入、成員與狀態）。只有它列在大廳，房間清單由它組出 `RoomInfo` |

- 主機每次房間有變化時呼叫 `UpdateAdvertisement`，內容不同才寫入 `a`。
- 主機同時是 Photon 的 master client：Photon 換了 master（原 master 離開時自己挑一位）而主機不是它時，主機用 `SetMasterClient` 拿回來。工作階段不採用 Photon 挑的人。
- `HostAddress` 在房間建好、主機進房後才有值；建房失敗時 `Status` 為 `Failed`，`LastError` 說明原因。

### 連線與事件

房間裡沒有「連線」，`PhotonTransport` 用事件在兩個 actor 之間建立邏輯連線。所有事件都是可靠、有序、channel 0、只送給指定的 actor（`TargetActors`）、不快取（`DoNotCache`），內容都是 byte[]：

| 代碼 | 名稱 | 內容 |
|---|---|---|
| 1 | Data | 一則工作階段訊息 |
| 2、3 | Ping、Pong | 送出者的時鐘（i64）；Pong 原樣送回 |
| 4 | Open | 客戶端 → 主機：傳輸版本（u8，目前 1）與連線編號（i32） |
| 5 | Accept | 主機 → 客戶端：接受的連線編號 |
| 6 | Close | 任一方：結束（或拒絕）的連線編號 |
| 7 | Fragment | 大訊息的一段：旗標（1 = 第一段，後面接總長度 i32），然後是資料 |
| 8 | Credit | 收件者 → 寄件者：連線編號（i32）與到目前為止收到的 Data、Fragment 位元組數（i64） |

- **連上主機**：客戶端進房後讀 `h`，對那個 actor 送 Open；主機回 Accept 時兩邊觸發 `PeerConnected`。`h` 指的 actor 不在房間裡（移轉中）時先等，直到房間記錄新的主機或 `ConnectTimeout`（20 秒）。不是主機的人收到 Open 會回 Close，傳輸版本不同的 Open 也一樣。
- **結束**：任一方 `Disconnect` 送 Close（排在已送出的資料後面，對方先收完再結束）；客戶端關閉連線時同時離開房間、斷開 Photon。主機 `Shutdown` 不送 Close，直接離開房間，所以客戶端看到的是 actor 離開，也就是 `DisconnectReason.HostLeft`。actor 離開房間（自己離開或被 Photon 判定逾時）時，另一方的連線以 `ConnectionLost`（主機端）或 `HostLeft`（客戶端）結束。
- 同一個 actor 再送 Open（重連）時，主機結束它舊的連線、開新的。連線結束後才到的訊息與這一方關閉後的訊息都會丟掉；對方關閉前送出的訊息照常送達。
- `Connect` 在前一次嘗試還沒結果時呼叫，前一次就放棄（以 `Local` 結束）：移轉時工作階段會在房間還沒記錄新主機前換下一位。

### 大訊息與流量控制

- 超過 `FragmentBytes`（48 KB）的訊息切成 Fragment 送出，收到最後一段才觸發 `MessageReceived`。上限 `MaxMessageBytes`（`NetProtocol.MaxMessageBytes`，1 MB）；超過時 `Send` 丟出 `ArgumentException`，收到超過的、或段落接不起來的，連線以 `ProtocolError` 結束。
- Photon 的伺服器替每個收件者排隊等著送出的資料，累積到接近 1 MB 時會把收件者踢掉（`SendBufferFull`）。快照或大型開局資料一次全丟出去就會這樣，所以每條連線最多只讓 `SendWindowBytes`（192 KB）在路上：其餘排在連線自己的佇列，收件者每收到一個 `FragmentBytes` 就回一次 Credit，寄件者再送下一批。`SendWindowBytes` 至少要兩個 `FragmentBytes` 再多 16 bytes，因為收件者還沒回報的部分可能接近一個 `FragmentBytes`。這一方關閉的連線會先把佇列裡的東西送完（最後是 Close）；對方離開房間或先送來 Close 時，佇列直接丟掉。同一則訊息送給多人時，下一筆相同的連線合成一個事件送出。
- 往返時間以 Ping/Pong 每 `PingInterval`（500 ms）量一次，量的是經過 Photon 轉送的實際往返；客戶端以它調整輸入延遲。

### 主機移轉的對應

| 傳輸層介面 | Photon |
|---|---|
| `SupportsHostMigration` | true：大家在同一個 Photon 房間裡，主機離開後房間還在 |
| `MigrationAddress` | 在房間裡的客戶端：`actor:{自己的號碼}` |
| `RoomHostAddress` | `h` 的 actor 在房間裡時 `actor:{h}`，否則 null |
| `IsPeerPresent` | `actor:N`：N 在房間裡；房間位址：`h` 的 actor 在房間裡（而且不是自己） |
| `HostLeft` | 客戶端連著的主機離開房間（或被 Photon 判定逾時） |
| `TakeOverHost(advertisement)` | `OpSetCustomPropertiesOfRoom({ h: 自己, a: 廣告 }, expected { h: 原主機 })`，由 Photon 伺服器比較後交換，同一時間只有一人成功。等伺服器回覆（約一個來回，最多 `TakeOverTimeout` 3 秒）：成功就成為主機（同時拿回 master client）；別人先成功就回傳 `actor:{他}`；沒等到回覆（或已不在房間）就回傳房間位址，工作階段會連到房間記錄的主機，若那是自己就再試一次接手 |
| `Connect("actor:N")` | 已在房間裡就不必重新加入，等 `h` 變成 N 再送 Open；`h` 變成別人就 `ConnectFailed`，讓工作階段改跟 `RoomHostAddress`。不在房間裡（自己的連線剛斷過）時先重新加入原本的房間 |

- **主機離開**：`Leave()` 送出 `HostLeaving` 後 `Shutdown`；其他人收完它最後的訊息就看到 `HostLeft`，接手通常在一秒內完成。
- **主機整台停住或斷網**：Photon 伺服器要等連線逾時（約 7～10 秒）才把它移出房間，所以客戶端自己先判斷：連著主機的客戶端 `HostSilenceTimeout`（3 秒）沒收到主機的任何事件（主機每 500 ms 送 Ping，暫停中也送），而自己和 Photon 伺服器的連線還正常（最近 1.5 秒內收到過伺服器的回應）時，把主機當作已離開：結束連線（`HostLeft`），移轉照常進行，`IsPeerPresent` 也不再把它算成在房間裡。所以主機當機後約 3～5 秒就由下一位接手。自己的連線也斷了的客戶端不這樣判斷，照一般斷線處理。`HostSilenceTimeout = TimeSpan.Zero` 關掉這個判斷；不為零時至少要兩個 `PingInterval`。
- **被當作離開的主機其實還活著**（例如手機切到背景超過 3 秒）：接手的人改了 `h`，它看到房間記錄的主機不是自己，就以 `HostReplaced` 設定 `HostingLost`，走下面「主機自己的連線斷了」的同一條路：存檔、停止，玩家再「回到對局」。客戶端關掉舊連線後不再接受舊主機的資料，所以對局不會分岔。
- **主機自己的連線斷了**：
  - 房間裡只有它：3 秒後以新的代碼重新開房（`HostAddress` 跟著改），工作階段照常；網路還沒恢復就再等更久（每次加倍，最多 30 秒）再試，直到開成。AppId 無效或地區不可用時不再試。
  - 房間裡還有別人：別人會移轉、繼續玩，這台不能再單獨推進同一場對局。傳輸設定 `HostingLost`（`Status` 為 `Failed`）；用戶端看到後呼叫 `NetworkMatchRunner.ReportHostingLost()`，對局存檔並以 `ConnectionLost` 停止，玩家再從房間清單「回到對局」，以一般玩家回到原本的部落。
- **客戶端自己的連線斷了**：`ConnectionLost`，工作階段照一般流程重連（新的 Photon 連線、重新加入房間、新的 actor 號碼）。

### 找房

`PhotonRoomBrowser` 連上一個地區後加入 Photon 的大廳，Photon 會送整份房間清單與之後的每一次變化；每個房間由屬性 `a` 組成 `RoomInfo`（位址 `region/CODE`，`Ping` 為到伺服器的往返）。代碼格式不對或沒有 `a` 的房間不列出。`Status`、`LastError`、`Region`（實際連上的地區）、`RegionPings`（找最近地區時量到的各地區延遲）供畫面顯示。連線中斷後每 5 秒自動重試；AppId 無效、地區不可用這類等待也不會好的錯誤，只在 `Refresh()` 時重試。

### 地區與連線方式

- `PhotonSettings.Region` 為 null 時，Photon 先 ping 各地區再選最近的；同一次執行裡記住結果，下一條連線只 ping 少數地區，連得比較快。不同地區的房間彼此看不到，所以房間位址帶著地區。
- 預設用 UDP；網路擋住 UDP 時自動改用 WebSocket Secure（TCP 443）重連一次。`UseWebSockets = true` 一開始就用 WSS。名稱伺服器的驗證本來就走 WSS。
- `PhotonRegions.Known` 是 Photon Cloud 的公開地區代碼；應用程式若在 Photon Dashboard 只開了部分地區，選到其他地區會得到 `RegionUnavailable`。

### 狀態與錯誤

連線是非同步的：`Status`（`Offline`、`Connecting`、`Connected`、`Failed`）與 `LastError` 隨時可讀，變化時在 `Poll` 裡觸發 `StatusChanged`。執行期的失敗都不丟例外，只有用錯（角色不對、位址格式錯、參數不合）才丟。

| `PhotonError` | 情況 |
|---|---|
| `NotConfigured` | 這個版本沒有嵌入 AppId |
| `NoInternet` | 連不到 Photon 的伺服器（沒有網路、DNS 失敗、連接埠被擋） |
| `Timeout` | 連線途中沒有回應 |
| `InvalidAppId` | Photon 不認得 AppId |
| `ServerFull` | 應用程式的同時連線數（CCU）已滿 |
| `RegionUnavailable` | 地區不存在或沒有開放給這個應用程式 |
| `RoomNotFound`、`RoomFull`、`RoomClosed` | 加入房間被 Photon 拒絕 |
| `HostNotAnswering` | 進了房間，但主機沒有回 Accept（或房間沒有主機） |
| `ConnectionLost` | 連上之後斷線 |
| `HostReplaced` | 這台主機斷線期間房間改由別人主持 |
| `ServerError` | 其他拒絕或伺服器端斷線（例如伺服器的佇列滿了） |

畫面上的文字見 `client/src/Ui/Net/InternetPlay.cs`（`multiplayer.internet.error.*`）。

### 診斷

`PhotonDiagnostics.Log` 收到傳輸的狀態變化與 Photon SDK 的警告、錯誤（SDK 的除錯訊息不轉出）。每一行先去掉 AppId 與 SDK 會印出的 AppId 前 8 碼，遊戲可以直接寫進紀錄；Photon 的背景執行緒也可能呼叫它。用戶端把它接到 `GD.Print`。

### 測試

- `tests/Tribes.Net.Photon.Tests` 用程序內的假 Photon 雲（`Support/FakePhotonCloud.cs`）：地區、大廳、actor 號碼、master client、屬性的比較後交換、有序的定向事件、伺服器替收件者排隊的上限與下載速度、乾淨離開、當機（伺服器逾時後移除）、斷網。以手動時鐘推進，每次結果相同。上面跑傳輸本身的測試，也跑未修改的 `HostSession`、`ClientSession`：大廳、對局同步、中途加入、離開再回來、重連、主機離開與當機時的移轉（含連續兩次、接手者沒反應、搶接手、移轉期間大量指令只執行一次）。
- 線上測試（`TRIBES_PHOTON_LIVE=1`，會用到 Photon Cloud，約 4 個 CCU）：主機與兩個客戶端在亞洲地區互送訊息、1 MB 訊息、以伺服器的比較後交換接手主機、房間清單、最近地區、WSS、UDP 被擋時改用 WSS、AppId 錯誤／房間不存在／地區錯誤的回報。`TRIBES_PHOTON_LOG=<檔案>` 會把診斷寫進檔案。
- 整套遊戲：`tools/session-internet-check.sh` 在本機開數個無畫面的遊戲（`scenes/session/internet_dev.tscn`），經 Photon Cloud 以真正的規則對打並逐 tick 比對雜湊。環境變數：`TICKS`（2000）、`CLIENTS`、`REGION`（asia）、`MODE`（`endless` 可中途加入）、`HOST_ARGS`、`LAST_ARGS`、`LATE_AT=TICK`（主機過了那個 tick 時再加入一位）、`KILL_AT=TICK`（在那個 tick 強制結束主機的行程，檢查移轉）。執行器的參數見 `client/src/Session/Dev/InternetDevRunner.cs`（`--net-quit-at` 主機中途離開、`--net-rejoin-at` 客戶端離開後從房間清單回來、`--net-resume=存檔` 把主機存檔開成網際網路房間、`--net-join-list` 從房間清單回到自己的對局）。每個遊戲的使用者資料（設定、存檔）放在 `OUT/appdata-<名稱>`，不會碰到平常玩的資料。四個人時主機加上 `HOST_ARGS="--net-computers=0"`，免得電腦部落占掉欄位。

```bash
dotnet build client/Tribes.Client.csproj
TICKS=2000 CLIENTS=3 HOST_ARGS="--net-computers=0" bash tools/session-internet-check.sh               # 4 人
TICKS=2000 CLIENTS=3 HOST_ARGS="--net-computers=0" KILL_AT=750 bash tools/session-internet-check.sh   # 主機當機
MODE=endless CLIENTS=2 LATE_AT=500 HOST_ARGS="--net-computers=0" bash tools/session-internet-check.sh # 中途加入
```

## 限制

- 主機自己的輸入延遲固定為 `MinInputDelayTicks`，客戶端依 RTT 調整，主機的操作反應較快。
- 主機不等落後的客戶端；連線差的玩家只會看到自己的操作較晚生效，不會拖慢其他人。
- 區域網路沒有主機移轉：主機離開就結束這次連線，對局要靠主機的存檔續玩。
- 主機的存檔可以在區域網路或網際網路上續玩（讀檔時選）；網際網路的對局平常不必靠存檔，主機離開時由其他人接手，接手的人之後照樣自動存檔，所以最後一位主機的存檔可以續玩。
- 網際網路對戰透過 Photon 的雲端轉送：每個在看房間清單或在房間裡的裝置算一個同時連線（CCU）。免費方案上限約 20，而且限定開發用途，正式公開前要換付費方案。
- 不同地區的房間彼此看不到：朋友要選同一個地區，或用帶地區的房間位址（`jp/K7QM2P`）。
- 主機當機或斷網時，約 3～5 秒後其他人才會接手；這段時間對局停著不動，畫面顯示「主機連線中斷，正在換手…」。主機停住超過 3 秒（例如手機切到背景）也會被換掉，它的玩家要「回到對局」。
- 中途加入與重新同步每次都傳完整快照，地圖越大越久（已壓縮，加入的人在房間畫面看得到進度）。世界解開後超過 256 MB 就無法再讓人加入或重新同步，加入的人與主機都會收到通知。
- 只有能下指令的玩家能要求暫停或改速度；觀戰者與已放棄的玩家不能。
