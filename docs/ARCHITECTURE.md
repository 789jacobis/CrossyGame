# Cross The Road 系統架構

## 執行架構

```mermaid
flowchart LR
    Player[玩家] --> Page[itch.io 遊戲頁面]
    Page --> Web[Unity Web Client]

    subgraph Client[Unity 客戶端]
        Web --> GameManager[GameManager]
        GameManager --> PlayerController[PlayerController]
        GameManager --> LaneGenerator[LaneGenerator]
        LaneGenerator --> Pools[車輛／浮木／火車物件池]
        GameManager --> ScoreManager[ScoreManager]
        Web --> UI[選單／暫停／排行榜 UI]
        Web --> Audio[GameAudioManager]
        Web --> Online[OnlineServicesManager]
        Web --> Local[(PlayerPrefs)]
    end

    Online --> Auth[UGS Authentication]
    Online --> CloudCode[Cloud Code C# Module]
    UI -->|讀取| Leaderboards[UGS Leaderboards]
    UI --> Names[UGS Player Names]
    CloudCode --> Protected[(Cloud Save Protected Data)]
    CloudCode -->|Service Token 寫入| Leaderboards
    Online -.->|Player Write 被拒絕| Access[Access Control]
    Access -.-> Leaderboards
```

## 成績提交流程

```mermaid
sequenceDiagram
    actor Player as 玩家
    participant Client as Unity Client
    participant Cloud as Cloud Code
    participant Save as Cloud Save Protected Data
    participant Board as Leaderboards

    Player->>Client: 開始遊戲
    Client->>Cloud: StartRun()
    Cloud->>Save: 儲存 RunId、開始時間、Consumed=false
    Cloud-->>Client: RunId

    Player->>Client: 遊玩並死亡
    Client->>Cloud: SubmitRunScore(RunId, Score)
    Cloud->>Save: 讀取執行紀錄
    Cloud->>Cloud: 驗證玩家、分數、時間與重複提交
    alt 驗證成功
        Cloud->>Board: 以 Service Token 寫入分數
        Cloud->>Save: 設定 Consumed=true
        Cloud-->>Client: accepted=true
    else 驗證失敗
        Cloud-->>Client: 拒絕提交
    end
```

## 安全邊界

- 玩家可直接讀取排行榜，以減少不必要的 Cloud Code 呼叫。
- `CrossyRoadAccessControl.ac` 禁止 `Player` 對 Leaderboards 執行任何寫入。
- Cloud Code 使用服務權杖寫入排行榜，不受玩家寫入限制影響。
- `RunId` 只能提交一次，避免同一局重放。
- 伺服器拒絕非正分、超過上限、超時或得分速度不合理的結果。
- Cloud Save Protected Data 不由一般客戶端修改。

## 部署單位

| 單位 | 位置 | 部署目的地 |
| --- | --- | --- |
| Unity Web Build | `Builds/Release/`（不進 Git） | itch.io |
| Cloud Code Module | `CrossyRoadServer/`、`Assets/CloudCode/CrossyRoadServer.ccmr` | UGS production |
| Access Control | `Assets/CloudCode/CrossyRoadAccessControl.ac` | UGS production |
| Unity Client Bindings | `Assets/CloudCode/GeneratedModuleBindings/` | 隨 Web Build 發布 |

## 已知限制

- 匿名帳號與瀏覽器本機資料可能因清除網站資料或更換裝置而改變。
- 目前驗證著重於執行票證、時間與分數合理性，並未在伺服器逐步模擬所有玩家操作。
- Cloud Code、Cloud Save 或 Leaderboards 暫時不可用時，核心玩法仍可運行，但線上提交與排行榜會受到影響。
