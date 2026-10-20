# Photon Realtime（第三方）

網際網路對戰用的 Photon Realtime 用戶端，取自 Exit Games 的 Photon .NET Client SDK **5.1.20**（2026-09-16）。只用遊戲需要的部分，內容沒有修改。SDK 的檔案不在儲存庫裡（Photon 的授權不允許轉散布），建置前要自己下載放進這個目錄；沒放好時整個方案都建置不起來，建置會停在 `PhotonRealtime` 專案並指出這份說明。

| 路徑 | 內容 | 來源 |
|---|---|---|
| `lib/netstandard2.0/PhotonClient.dll`、`.xml` | SDK 的 `PhotonLibs/Release/netstandard2.0`：底層連線（UDP、TCP、WebSocket） | 自己放 |
| `PhotonRealtime/*.cs` | SDK 的 `PhotonRealtimeApi` 原始碼（Unity 專用的 `CustomTypesUnity.cs`、`PhotonUtilitiesUnity.cs` 不放） | 自己放 |
| `license.txt` | SDK 附的授權說明 | 自己放 |
| `PhotonRealtime/PhotonRealtime.csproj` | 本專案寫的專案檔：把上面的原始碼編成 `PhotonRealtime.dll`（netstandard2.0）；第三方程式碼不套用本專案的分析與警告規則 | 儲存庫 |

## 放進 SDK

1. 登入 Photon 帳號，在 <https://www.photonengine.com/sdks> 的 Realtime「.NET」下載 5.1.20（`photon-dotnet-sdk_v5-1-20.zip`），解壓縮。
2. SDK 的 `PhotonLibs/Release/netstandard2.0/PhotonClient.dll` 與 `PhotonClient.xml` 複製到 `lib/netstandard2.0/`。
3. SDK 的 `PhotonRealtimeApi/*.cs` 複製到 `PhotonRealtime/`，再刪掉 `CustomTypesUnity.cs`、`PhotonUtilitiesUnity.cs`。
4. SDK 附的授權說明複製成 `license.txt`。

`.gitignore` 已排除這些檔案，不要提交它們。

- 授權：Photon 的使用條款（<https://www.photonengine.com/terms>）。免費方案有同時連線數（CCU）上限，且條款把免費方案限定在開發用途；正式對外發行前要依人數升級方案。
- 只有 `src/Tribes.Net.Photon` 參考這些檔案；`Tribes.Net` 本身不依賴 Photon。
- 換新版 SDK：用新版的同名檔案覆蓋 `lib/` 與 `PhotonRealtime/*.cs`（保留 `PhotonRealtime.csproj`），更新本檔與 `PhotonRealtime.csproj` 的版本號，再跑 `tests/Tribes.Net.Photon.Tests`（含連上 Photon 的線上測試，見 docs/NETWORK.md）。
