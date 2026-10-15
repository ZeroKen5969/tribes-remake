# 素材與圖像模型

原版的圖片、精靈組裝資料、字型、字串與音樂，轉成 Godot 用戶端可直接使用的檔案（`data/assets/`）與資料（`data/client/`）。`src/Tribes.Graphics` 讀這些資料，依動畫、面向、幀與外觀算出要畫的部件，與引擎無關。

`data/` 是內容儲存庫；Godot 讀的是複製到 `client/assets/`（`res://assets`）的那份，由 `tools/sync-assets.sh` 複製（`tools/godot-bg.sh`、打包腳本與用戶端建置會自動執行）。要改素材就改 `data/assets/`。產生素材要用原版儲存庫（`original/`）的匯出工具與原版資源。

## 產生方式

```bash
dotnet run --project original/tools/Tribes.AssetExport -c Release -- \
  --res original/game/res --root . \
  --cjk-font work/downloads/cubic-11 \
  --soundfont work/downloads/fluidr3-gm-extracted/FluidR3_GM.sf2
```

| 選項 | 說明 |
|---|---|
| `--res` | 原版資源目錄（`original/game/res`） |
| `--root` | 輸出根目錄（寫入 `data/assets/` 與 `data/client/`） |
| `--cjk-font` | 含 `Cubic_11.ttf` 與 `OFL.txt` 的目錄；先檢查 SHA-256 再複製 |
| `--soundfont` | General MIDI SoundFont（.sf2）；省略時只輸出 MIDI，既有的 OGG 與 `music.json` 不動 |
| `--previews 目錄` | 另存各類精靈、特效、動畫、物件、面板的編號對照圖（查編號用） |

外部檔案的取得方式見「第三方素材」。所有輸出都可重複產生，同樣的輸入得到同樣的檔案。

驗證資料與測試：

```bash
bash original/tools/asset-ref/run.sh           # 以原版 bytecode 重畫參考資料（cases、maps 或 all）
dotnet test tests/Tribes.Graphics.Tests
dotnet test tests/Tribes.Graphics.Tests --collect:"XPlat Code Coverage" --results-directory work/coverage-graphics
```

`original/tools/asset-ref` 用原版遊戲（`original/reference/game-remapped.jar`，與 DiffHarness 相同的類別載入器與虛擬時鐘）自己的繪圖函式畫出每個測試案例，寫成 `tests/Tribes.Graphics.Tests/Data/reference-cases.txt.gz`（每行：案例參數 | 畫出像素的外框與 CRC32）；地圖畫面由隨機按鍵遊玩擷取到 `Data/maps/`，同一組按鍵在 DiffHarness 下必須得到逐幀相同的畫面。測試以 `Tribes.Graphics` 組出同樣的案例、逐像素比對。

## 輸出檔案

| 路徑 | 內容 |
|---|---|
| `data/assets/original/pi0_00.png`…`pi0_26.png` | 精靈圖集（25 大字型、26 小數字） |
| `data/assets/original/pi9_00.png`…`pi9_05.png` | 換色圖集（部落與外觀變體） |
| `data/assets/original/pi8_00.png`…`pi8_11.png` | 劇情插圖 |
| `data/assets/original/l0.png`…`l3.png`、`icon.png` | 啟動畫面、程式圖示 |
| `data/assets/fonts/original-large.fnt` / `.png` | 原版大字型（BMFont） |
| `data/assets/fonts/original-small-yellow`／`red`／`cyan` `.fnt` / `.png` | 原版小數字字型的三種顏色（BMFont） |
| `data/assets/fonts/cubic-11/Cubic_11.ttf`、`OFL.txt` | 繁體中文像素字型與授權 |
| `data/assets/music/song-0.ogg`…`song-4.ogg` | 五首配樂（Ogg Vorbis，44.1 kHz 立體聲） |
| `data/assets/music/midi/song-0.mid`…`song-4.mid` | 原版 MIDI |
| `data/client/sprites.json` | 精靈與動畫模型 |
| `data/client/terrain-tiles.json` | 地格 0..255 的畫法 |
| `data/client/ui.json` | 面板、介面圖示、顏色、整張圖片 |
| `data/client/fonts.json` | 原版點陣字的排字規則 |
| `data/client/music.json` | 歌曲與播放時機 |
| `data/client/strings/en.json`、`fr`、`it`、`de`、`es`、`zh-TW` | 各語言字串 |

圖片保留原檔的像素資料與調色盤，只拿掉 gAMA、tEXt 等不影響像素的區塊，任何解碼器讀到的都是原版顯示的顏色。原圖只有完全不透明或完全透明的像素。Godot 以 Lossless 匯入、最近鄰取樣（專案已設 `default_texture_filter=0`）即保持原樣。

JSON 一律 UTF-8、camelCase 欄位名、列舉值為 camelCase 字串、顏色為 `#rrggbb`，不適用的欄位省略。每個清單以 id 為索引（第 `i` 筆的 id 是 `i`）。C# 端用 `Tribes.Graphics.Serialization.CatalogJson.Read<T>()` 讀取。

## 座標與錨點

- 地圖格 22×16 像素。地圖畫面（view）原點是畫面上第一格的左上角；格 `(c, r)` 的左上角在 `(c * 22, r * 16)`。
- 所有組合結果（`DrawPart`）的座標都相對於「錨點」，畫的時候加上錨點即可：
  - 地格：格的左上角。
  - 村民、動物：所在格的左上角加上格內像素位移（單位欄位 2、3；動物欄位 2、3）。動畫幀自己的位移已含在結果裡。
  - 建築：佔地左上角那一格的左上角。資源點、陷阱、地形物件：所在格的左上角。
  - 特效：呼叫者給的點（例如單位的軀幹錨點）。
- 原版的地圖緩衝四周多留左右 11、上下 8 像素，結果不含這段邊界。
- 正常上方資源列時，地圖畫面原點在螢幕 `(0, 16)`（列高 22，地圖往上收 6 像素，第一列的上緣被列遮住）；上方列展開（面板 4）時在 `(0, 48)`。見 `ui.json` 的 `mapView`。
- 原版的繪製順序：
  1. 地形層：列由上而下、每列由左而右，畫每格的地格，再畫該格物件（建築、資源點、陷阱）的地面部分。
  2. 物件層：列由上而下（從第 -1 列到可見列 + 2）、每列由右而左，逐格畫物件、單位、動物與該格的戰爭迷霧。
- 原版從不鏡像翻轉圖片；每個方向都有自己的圖。`DrawPart.FlipX` 永遠是 false。

## 精靈模型（`sprites.json`）

| 區段 | 內容 |
|---|---|
| `sheets` | 圖集：`id`（`pi0_05`）、檔案、尺寸。前 27 筆是精靈圖集 0..26，之後是換色圖集 |
| `paletteVariants` | 換色變體 0..5 各自替換哪些圖集 |
| `primitiveColors` | 線、框、色塊部件用的 14 種顏色 |
| `pieces` | 1810 個部件：`image`（圖集 `sheet` 上的矩形 `x, y, width, height`）、`line`（`x, y` 到 `endX, endY`）、`outline`（涵蓋 `width + 1` × `height + 1`）、`fill`（位置與大小乘 `scale`，選單大背景為 4）、`none` |
| `sprites` | 1721 個組合精靈，已攤平成部件清單：`{piece, x, y}`，依序畫；有陰影的精靈第一個部件是陰影。地格 0..174 就是同編號的精靈 |
| `effects` | 144 個特效動畫：每幀 `{sprite, x, y}`；顯示第 `tick % 幀數` 幀 |
| `bodyAnimations` | 162 個村民與動物動畫：`category`、`directionCount`（1、2、4、8）、`framesPerDirection`、`triggerFrame`／`triggerParameter`（動作生效的幀與效果），`frames` 依方向排列：方向 `d` 的第 `f` 幀是 `frames[d * framesPerDirection + f]`，每幀 `{pose, x, y}` |
| `directionMaps` | 面向 0..7（0 東，順時針：1 東南、2 南…7 東北）對應到方向編號，依方向數分 4 組 |
| `poses` | 823 個姿勢：槽位 `{type, source, id, x, y}`，`source` 為 `piece` 或 `sprite`，`id` 0 表示空槽、1 表示只有動物畫 |
| `objectGraphics` | 119 個物件圖：部件 `{kind, group, stage, id, x, y, shiftX, shiftY}`（說明見下） |
| `objectGraphicFirstParts` | 各物件圖第一個部件在原版單一部件表中的位置 |
| `projectiles` | 投擲武器（石塊 1、長矛 2、戰斧 5）飛行中的精靈：`firstSprite + frameOffsets[方向 * directionStride + 飛行幀]` |
| `tables` | 規則用的資料表：任務類別、頭飾、武器槽、突刺位移、住戶面向、倉庫堆、建築佔地 |

### 槽位與外觀（村民）

| 槽位 `type` | 內容 | 外觀規則（成人） |
|---|---|---|
| `torso` | 軀幹與衣著 | 精靈 + 軀幹樣式 × 72（樣式 2、3 是 0、1 的換色版，用變體 0）＋ 電腦方 36 ＋ 女性 18 |
| `head` | 頭與頭髮 | 精靈 + 列 × 12（女性 1；男性 30 歲以下 3、50 歲以下 2、其他 0）；髮色 1..3 用變體 1..3，電腦方一律變體 3 |
| `headwear` | 頭飾 | 玩家領袖 +2；其他依任務類別查 `headwearByTaskClass`，0 表示不畫，否則 + 值 - 1 |
| `clubWeapon`、`axeWeapon`、`spearOrStone` | 手上武器 | 依動畫 `category`：`weaponBySlot` 只畫該武器所屬的槽（骨錘、戰斧是同槽 +1）；`weaponInClubSlot`、`carrying` 由 `clubWeapon` 槽 + 武器編號畫 |
| `load` | 搬運物 | `carrying` 動畫且有搬運物時 + 搬運物類型 |
| `shadow` | 影子 | 選取時 +1（電腦方 +3）；隊伍成員先畫標記精靈 599 |
| `backArm`、`frontArm`、`legs` | 手臂、腿 | 不變 |

電腦方的其他槽位用變體 4。小孩（未滿 12 歲）：男孩不畫頭飾、不畫棍棒槽，電腦方軀幹 +1，頭部 + (髮色 & 1)，不換色。動物的槽位照原樣畫。

外觀輸入是 `VillagerAppearance`：部落、年齡、性別、軀幹樣式與髮色（單位欄位 51 的位元 2-3 與 0-1）、是否領袖、任務（欄位 64）、武器（欄位 65：0 棍棒、1 石塊、2 長矛、3 石斧、4 骨錘、5 戰斧、9 空手）、搬運物（欄位 35）與數量（欄位 36）、選取標記。

### 物件部件

| `kind` | 作用 |
|---|---|
| `ground` | 地形層，逐格畫。`stage` 0 的地面部件開始一格的一段，後面 `stage` 大於 0 的地面部件在物件達到該階段時疊上去 |
| `fixture` | 物件層的固定圖，階段達到 `stage` 才畫 |
| `idleAnimation` | 該 `group` 的住戶沒在工作時播放的特效 |
| `workAnimation` | 住戶工作時播放；連續幾個這種部件依住戶的工作狀態挑一個，時鐘用住戶的動畫幀 |
| `ambientAnimation` | 一直播放：`group` 1 旗子（電腦方的特效編號 +1）、2 升級或修理中的揚塵、其他為煙 |
| `occupantAtWork`、`occupantIdle`、`occupantResting` | 住戶工作中、閒置、休息時畫在這裡（每個 `group` 只畫一人） |

`stage` 是建築等級、資源點的剩量階段或陷阱狀態。`shiftX`／`shiftY` 是額外的 128 像素倍數位移；地圖物件只用 `shiftX`，而且地面部件不加。

物件圖編號：建築種類 `k` 為 `k + 1`；資源點種類 `k`（1..7）為 `k + 26`；陷阱種類 `k`（20 網、21 彈簧、22 坑、23 已下單未放置）為 `k + 1`；地格 175..255 為 `地格 - 146`；標題畫面景物為 118。

### 組合函式（`Tribes.Graphics.Composition`）

```csharp
var catalog = CatalogJson.Read<SpriteCatalog>("data/client/sprites.json");
var composers = new Composers(catalog);

// 村民：動畫、面向、幀、外觀
BodyComposition body = composers.Bodies.Compose(new BodyRequest(BodySubject.Villager, animation: 0, facing: 2, frame: 1)
{
    Appearance = new VillagerAppearance { Tribe = Tribe.Computer, Age = 34, Weapon = 2 }.WithAppearanceBits(bits),
});
foreach (DrawPart part in body.Drawing.Parts) { /* 以錨點 + part.X/Y 畫 part.Sheet 的 (SourceX, SourceY, Width, Height) */ }

// 建築：地形層每格一次、物件層每棟一次
ObjectComposition ground = composers.Objects.Compose(ObjectRequest.Building(kind, level, slot, ObjectPass.Ground) with { CellIndex = dy * width + dx });
ObjectComposition upper = composers.Objects.Compose(ObjectRequest.Building(kind, level, slot, ObjectPass.Upper) with
{
    Tick = frameCounter, MapRow = row, Occupants = occupants, PlayerStock = stock,
});

DrawList effect = composers.Sprites.ComposeEffect(77 + thought, tick);
DrawList tile = composers.Terrain.ComposeTile(tileId);
```

- `BodyComposition` 另回傳軀幹錨點（單位效果畫在這）與頭部錨點（想法泡泡、任務圖示、生命條畫在這）。`BodyComposer.EffectSlotOffset` 給出原版附在單位某槽位上的特效位置。
- `BodyRequest.Portrait` 畫圖示列用的頭像（動畫 8 的第 3 個存放幀，小孩用動畫 14 並上移 6 像素）；`InsideBuildingPose` 是住戶拿著東西閒置時的姿勢。
- `ObjectRequest` 的工廠函式（`Building`、`ResourceNode`、`TerrainObject`、`Trap`、`Preview`）照原版填好規則讀的欄位；`MenuScene.Request` 是標題畫面景物與三個村民。
- `PartRasterizer` 把部件畫進 `RgbaImage`（工具與測試用），`Png` 讀寫 PNG。

### 原版的特殊行為（照樣保留）

- 換色變體會延續：原版在村民跳過某些槽位後不重設變體，接著畫的東西沿用它。每個組合結果都回傳 `PaletteVariantAfter`，要重現時把它傳給下一個組合的 `PaletteVariant`。
- 負的特效時鐘（原版的餘數保持負號）會取到前一個特效的幀；超過一個方向長度的幀號會取到下一個方向或下一個動畫的幀。
- 引用不存在的精靈、姿勢、部件時，原版的繪圖在例外處停止：組合結果標記 `Truncated` 並只含之前的部件；單位在建築內出錯時只停止那個單位。
- 投擲場（建築種類 5）的放置預覽套用倉庫的存量堆規則（規則種類等於物件圖編號 6）；資源點讀取「與資源點同編號的建築欄位」決定揚塵與愛心；電腦方陷阱在地形層用物件層畫法，所以看不到。
- 突刺動畫（141..144）的位移取單位欄位 6；畫動物時原版讀的也是單位表，用 `BodyRequest.LungeFacing` 重現。
- 物件層煙霧動畫的時鐘含「觸發繪製那一格的地圖列」（`ObjectRequest.MapRow`）與部件在原版部件表的位置。

## 地格（`terrain-tiles.json`）

每個地格 0..255 一筆：

| 欄位 | 內容 |
|---|---|
| `drawing`、`graphic` | `sprite`（0..174，畫同編號精靈）或 `objectGraphic`（175..255，物件圖 `地格 - 146`） |
| `ground` | 地形層要畫的部件，已解到圖集矩形，相對於格的左上角；可能超出本格 |
| `overlay`、`overlayAnimations` | 物件層的固定圖與動畫（樹與岩石頂部，只有地格 180..231 與 243）。動畫時鐘：`ambientCount` 為 0 時是遊戲計數 `t`，否則 `(t + ambientCount * 地圖列 + clockConstant) mod 120` |
| `blocksMovement`、`isRock` | 不可通行（精靈的障礙旗標，或地格 175 以上）、岩石 |
| `usedInMaps` | 原版地圖是否用到 |

地圖檔存的地格 0、3、6 在載入時各加上隨機的 0..2（`randomVariantLastTile`、`randomVariantCount`）。

## 介面（`ui.json`）

- `panels`：20 個面板（資源列、訊息列、圖示列、小地圖、選單、對話框、軟鍵列…）。`area` 是面板在 240×320 畫面上的區域，`frame` 是一起畫的上緣（精靈 524）或下緣（528）框，`parts` 是版面部件：`piece`（畫部件）、`valueSprite`（精靈 + 標籤值，表示狀態的圖示）、`animation`（特效）、`special`（程式填入：0..2 數字、3..4 文字框、5..6 標籤、10 劇情插圖、11 小地圖）、`clip`（之後的部件裁到該部件的矩形）。`level` 是重畫等級，`tableIndex` 是原版部件表中的位置。
- `elements`：程式直接用編號畫的介面圖，分組列出編號、用途、畫在哪、怎麼選，以及每個編號的外框（相對於畫的位置）：游標、放置游標、目標標記、指令回饋、建築四角、隊伍標記、任務圖示（577 + 任務類別）、想法泡泡（77 + n）、單位效果、生命條（607 + 長度）、技能星、目前單位箭頭、戰爭迷霧（部件 11、12）、全軍令提示、揚塵、陷阱閃爍、選單箭頭、數量箭頭、圖示列格子與物品、費用圖示、文字內圖示（560 + n）、面板框、標題畫面。
- `colors`：生命條三色、選取框、建築進駐方塊、標題背景、小地圖各類顏色。
- `images`：劇情插圖、啟動畫面、程式圖示。

## 字型

- `fonts.json`：原版排字規則。字串檔每個位元組以 `glyphByByte` 轉成字形碼（列 × 30 + 欄）。大字型（0，圖集 25）三列字高 12、13、14，從同一行頂端畫；小字型（1，圖集 26）只有數字與正負號，三種顏色上下排列。字寬由各字形的 `x`／`width`，畫完前進「寬 + 字型編號」像素。第 0 列第 14 欄起是重音符：畫在前一個字的中央上方（往上 `raise`，負值往下），不前進。字形碼 126 窄退格（退回 字型編號 + 2）、127 換行、90..112 行內圖示（精靈 560 + 碼 - 90，佔 15 像素）、113 起數值列。`spellings` 列出字串檔怎麼拼非 ASCII 字元（`$`=ß、`<`=¡、`>`=¿、`~`=©，重音字母為「字母 + 重音位元組」）。
- `original-large.fnt`：大字型的 BMFont，含 ASCII、大小寫（同一組大寫字形）與所有可組出的重音字母（依原版的置中規則預先合成），行高 17。
- `original-small-*.fnt`：小數字字型三種顏色。
- `cubic-11/Cubic_11.ttf`：繁體中文像素字型，涵蓋六種語言的全部字串；以 M⁺ gothic 12r 為基礎的 12 像素點陣字，以 12 的整數倍字級使用並關閉反鋸齒。

## 字串（`strings/*.json`）

每種語言一個檔，`s`（表 s，0..227：選單、建築、武器、名字）、`t`（表 t，0..70：說明與提示）、`d`（表 t 從 71 起：教學與劇情，id 即遊戲使用的 t 索引）。每筆有：

- `text`：Unicode 文字，換行為 `\n`，重音字母已組合，`{N}` 為控制碼（0..22 行內圖示、23..30 數值列），`{name}` 在執行時代入版本號。
- `raw`：字串檔原樣（一個位元組一個字元）：`&` 換行、`=` 窄退格、重音位元組接在字母後，用原版點陣字排字時以這個為準。

原版字串以小寫存放，原版字型一律畫成大寫。`zh-TW` 取自 `original/game/res/5`（由 `original/game/i18n/zh-TW` 建置），控制碼與英文版相同。

## 音樂（`music.json`）

五首原版 MIDI（`sa` 依 `ResourceLoader` 寫死的起點切出），以 SoundFont 合成為 OGG（尾端多留 2 秒餘音，峰值超過 0.95 時整首等比例降低）。`moods` 是原版的選曲：`normal`（選單與一般遊玩，0、1 隨機）、`tension`（地圖畫面上有全軍令進行中，或電腦方的進攻波正指向關卡設定的目標，2、3）、`combat`（地圖畫面上 50 幀內有戰鬥，4）；一首播完再隨機挑一首。原版沒有音效，只有這五首配樂。

## 第三方素材

| 項目 | 版本與來源 | 授權 | 檢查值 |
|---|---|---|---|
| Cubic 11（俐方體11號） | v1.500，https://github.com/ACh-K/Cubic-11 的 `fonts/ttf/Cubic_11.ttf` 與 `OFL.txt`（tag v1.500） | SIL Open Font License 1.1，隨字型附 `OFL.txt`，不可單獨出售字型檔 | git blob `48b3411ce6f6f54db45b6125d108b574ebdc973e`；SHA-256 `0193f5f033612496df6b45ee92ac3b335bc6a5a24ff95da55ca87b33e57dcf62` |
| FluidR3_GM.sf2（只用於合成，不隨用戶端發佈） | Debian 原始碼套件 fluid-soundfont 3.1：http://deb.debian.org/debian/pool/main/f/fluid-soundfont/fluid-soundfont_3.1.orig.tar.gz | MIT（Copyright 2000-2002, 2008 Frank Wen） | tar.gz SHA-256 `2621acaa1c78e4abdb24bdd163230cc577e61276936d6aa6e3180582142f0343`（見已簽章的 `fluid-soundfont_3.1-6.dsc`）；sf2 SHA-256 `74594e8f4250680adf590507a306655a299935343583256f3b722c48a1bc1cb0` |
| MeltySynth | NuGet 2.4.1（https://github.com/sinshu/meltysynth） | MIT | |
| OggVorbisEncoder | NuGet 1.2.2（https://github.com/SteveLillis/.NET-Ogg-Vorbis-Encoder） | MIT | |
| NVorbis（只在測試中解碼 OGG） | NuGet 0.10.5 | MIT | |

取得外部檔案（每個下載放在自己的新目錄）：

```bash
mkdir -p work/downloads/cubic-11 && cd work/downloads/cubic-11
curl -LO https://raw.githubusercontent.com/ACh-K/Cubic-11/v1.500/fonts/ttf/Cubic_11.ttf
curl -LO https://raw.githubusercontent.com/ACh-K/Cubic-11/v1.500/OFL.txt
cd - && mkdir -p work/downloads/fluidr3-gm && cd work/downloads/fluidr3-gm
curl -O http://deb.debian.org/debian/pool/main/f/fluid-soundfont/fluid-soundfont_3.1-6.dsc
curl -O http://deb.debian.org/debian/pool/main/f/fluid-soundfont/fluid-soundfont_3.1.orig.tar.gz
sha256sum fluid-soundfont_3.1.orig.tar.gz      # 與 .dsc 的 Checksums-Sha256 相同
```

再從 tar.gz 取出 `fluid-soundfont-3.1/FluidR3_GM.sf2`、`COPYING`、`README` 到 `work/downloads/fluidr3-gm-extracted/`（例如用 Python 的 `tarfile` 只讀取這三個檔）。

渲染出的 OGG 由原版 MIDI 與 FluidR3 音色合成；原版遊戲內容（圖片、MIDI、字串）版權屬 THQ／Gear Games。
