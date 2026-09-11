# Cross The Road

一款以 Unity 6 與 C# 製作、受到經典過馬路玩法啟發的 2D 無限前進網頁遊戲。玩家需要穿越公路、河流與鐵路，避開車輛及火車，並持續刷新最高分與線上排行榜。

> 專案目前仍在開發中；遊戲名稱、美術與公開部署位置都可能調整。

## 一開始的專案目標

這個專案一開始不是以做出大型商業遊戲為目標，而是要完成一個「小而完整、可公開遊玩、能用於遊戲工程師面試」的作品集，重點包括：

- 使用 Unity 與 C# 建立完整、可反覆遊玩的遊戲循環。
- 將成品輸出成 Web 版本，讓使用者直接在瀏覽器遊玩。
- 練習後端與線上服務，包括玩家身分、名稱、成績提交與排行榜。
- 展示資料驅動設計、物件池、程序生成、非同步服務整合與基本防作弊思維。
- 保持架構可替換，日後學習 AWS 時能逐步遷移，而不必重寫整個 Unity 客戶端。

最初規劃使用 ASP.NET Core Web API、PostgreSQL、Entity Framework Core 與管理後台自行建置後端。為了先完成可展示的端到端版本，目前改以 Unity Gaming Services（UGS）實作匿名登入、玩家名稱與排行榜；自建後端仍保留為後續學習與擴充方向。

更完整的原始規劃可參考 [`crossy-game-project-handoff.md`](crossy-game-project-handoff.md)。

## 目前完成的功能

### 遊戲玩法

- 上、下、左、右格狀移動與跳躍動畫。
- 按照玩家進度持續生成並回收道路區段。
- 草地、公路、河流與鐵路 Lane。
- 汽車、卡車、巴士、浮木、火車及固定障礙物。
- 車輛、浮木與火車物件池。
- 碰撞、落水、死亡、重新開始、暫停與倒數流程。
- 依前進最遠距離計分，避免後退重複得分。
- 隨進度調整的難度階段。

### UI 與音效

- 主選單、玩家名稱輸入、排行榜、暫停與遊戲結束畫面。
- 鍵盤與滑鼠 UI 操作。
- 音樂與音效統一開關，設定會保存在本機。
- 主選單與遊戲中的背景音樂，以及跳躍、碰撞、落水、火車等音效。
- Web 版本支援中文輸入法與中文玩家名稱。

### 線上功能

- Unity Authentication 匿名玩家身分。
- Unity Player Names 顯示名稱。
- Unity Leaderboards 全球最高分排行榜。
- 顯示最高分玩家；玩家不在前段名次時，以省略列加上自己的排名。
- 同分玩家顯示並列名次。
- 離線時保留待上傳最高分，服務恢復後再次提交。

## 操作方式

| 操作 | 按鍵 |
| --- | --- |
| 移動 | 方向鍵或 WASD |
| UI 選擇 | 方向鍵／W、S |
| UI 確認 | Enter／Space 或滑鼠左鍵 |
| 暫停 | 畫面右上角暫停按鈕 |

## 技術與版本

- Unity `6000.3.10f1 LTS`
- Universal Render Pipeline 2D
- C#
- Unity Input System
- TextMesh Pro
- Unity Gaming Services：Authentication、Player Names、Leaderboards
- WebGL Input（處理瀏覽器中的 IME／中文輸入）
- 最終平台：Unity Web Build

## 專案結構

```text
Assets/
  Animations/       動畫與 Animator 資源
  Art/              角色、環境、車輛與 UI 美術
  Audio/            背景音樂與音效
  Data/             ScriptableObject 與遊戲設定資料
  Fonts/            中文字型與 TMP Font Asset
  Prefabs/          遊戲物件 Prefab
  Scenes/           遊戲場景
  Scripts/          遊戲、UI、音效與線上服務程式碼
Packages/           Unity 套件清單與鎖定檔
ProjectSettings/    Unity 專案設定
```

`Library/`、`Temp/`、`Logs/`、`UserSettings/` 與 `Builds/` 都是本機或建置產物，不會提交到 Git。

## 在 Unity Editor 執行

1. 安裝 Unity Hub 與 Unity Editor `6000.3.10f1 LTS`。
2. 為該 Editor 安裝 Web Build Support。
3. 用 Unity Hub 開啟此 repository 根目錄。
4. 等待 Unity 還原 Packages 並完成首次匯入。
5. 開啟 `Assets/Scenes/SampleScene.unity`。
6. 按下 Play 測試遊戲。

線上功能需要專案連結至對應的 Unity Cloud Project，並在 UGS Dashboard 建立 ID 為 `global_high_scores` 的 Leaderboard。沒有可用的 UGS 環境時，核心遊戲仍可離線執行，但線上名稱與排行榜功能會不可用。

## 建置 Web 版本

1. 在 Unity 選擇 `File > Build Profiles`。
2. 選擇 `Web` 並切換平台。
3. 確認 `Assets/Scenes/SampleScene.unity` 位於 Scene List。
4. Player Settings 的預設 Canvas 建議使用 `960 × 540`。
5. 按下 Build，輸出到被 Git 忽略的 `Builds/` 資料夾。
6. 使用本機 HTTP server 或網站平台提供服務；Web Build 不能直接以 `file://` 正確執行。

## 後端取捨與安全性

目前 Unity 客戶端直接使用 UGS SDK 完成匿名驗證、名稱與排行榜，因此 UGS 已經是現階段的託管後端。不過，客戶端提交的分數仍可能被修改，現在的版本不等同於完整的伺服器權威防作弊系統。

下一階段預計加入 UGS Cloud Code（C#）：

1. 由伺服器建立一次性的 `RunId`。
2. 記錄開始時間、遊戲版本與必要規則。
3. 結束時檢查 `RunId`、耗時與分數合理性。
4. 僅由伺服器將通過驗證的結果寫入排行榜。

這項設計會在 Unity 端包一層後端介面，避免遊戲邏輯直接依賴特定供應商。

## 未來 AWS 遷移方向

目前使用 UGS 不會阻礙之後學習 AWS。可以分階段替換：

- Unity Web Build：Amazon S3 + CloudFront。
- Cloud Code／遊戲 API：API Gateway + AWS Lambda（C#）。
- 排行榜與遊戲紀錄：DynamoDB，或依查詢需求使用 PostgreSQL。
- 玩家驗證：需要正式帳號與跨裝置登入時再評估 Amazon Cognito。

先保留統一的客戶端服務介面，就能讓 UGS 與 AWS 實作在遷移期間並存，逐項驗證後再切換。

## 下一步

- 加入 Cloud Code 成績驗證與一次性 `RunId`。
- 增加服務層介面與測試替身，降低 UGS 耦合。
- 補齊公開 Web 遊戲網址、展示影片與架構圖。
- 建立正式 Build 與部署流程。
- 盤點並標示所有美術、字型、音樂與音效的來源及授權。

## 資產與授權提醒

本 repository 尚未宣告通用的開源授權。專案中的字型、音樂、音效與美術可能各自受不同授權條款約束；公開散布或再利用前，請逐一確認其來源與授權範圍。
