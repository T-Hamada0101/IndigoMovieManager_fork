# Implementation Plan: 外側の運転席 Coordinator運転席化（2026-03-09）

## 1. 目的
- サムネイル制御の運転席を `MainWindow` から外部側へ移す。
- 既存の `ThumbnailProgressViewer.exe` を、閲覧専用 viewer から「監視 + 制御」UI へ育てる。
- 並列数、巨大動画閾値、GPU使用、運転モード、セッション限定の一時増減を、Coordinator 側の正規責務にする。

## 2. この計画の結論
- 最終形は `MainWindow -> ThumbnailCoordinator.exe -> ThumbnailWorker.exe x N` の3層とする。
- `MainWindow` は Control Plane の入口だけを持ち、重い判断はしない。
- `ThumbnailCoordinator.exe` は外側の運転席を内包し、運転方針の最終決定、QueueDB リース、スロット配分、Worker 起動管理、health/control/stats snapshot 出力を一元管理する。
- `ThumbnailWorker.exe` は Coordinator から渡された 1ジョブを実行する executor へ寄せ、QueueDB 読取、キュー分類、並列配分の判断を持たない。
- サムネイル Engine は「1ジョブをどう生成するか」を担う実行中核として残し、Coordinator の内部判断を再実装しない。

## 3. 背景
- 現状の外部 progress viewer は、`thumbnail-progress-*.json` と `thumbnail-health-*.json` を読む閲覧専用である。
- 一方で、並列数や role 別配分の最終判断はまだ `MainWindow` と `Worker` に分かれている。
- そのため、外部化したのに「表示は外側、運転席は内側」という中途半端さが残っている。
- ここを詰め切るには、viewer を Coordinator 運転席へ昇格させるのが筋である。

## 4. 対象範囲
- 外側運転席 UI の新設または既存 viewer の拡張
- `ThumbnailCoordinator.exe` 新設
- Worker supervisor の本体外移譲
- control snapshot / command snapshot の設計
- Worker の executor 化方針

## 5. 対象外
- WhiteBrowser 互換 DB 本体変更
- QueueDB の全面作り直し
- サムネイル生成エンジン本体の全面刷新
- Watcher 全体の外部化

## 6. 役割分担

### 6.1 MainWindow
- MainDB を開く
- 基本設定の入口を持つ
- Coordinator 起動/停止を行う
- 要約状態だけを表示する
- 外側の運転席を開く

### 6.2 ThumbnailCoordinator.exe
- 外側の運転席 UI を持つ
- QueueDB を読み、ジョブをリースして分類する
- `FastSlot` / `SlowSlot` の実効並列を決める
- Worker 起動本数と役割を決める
- `ffmpeg.exe` の同時実行制限を管理する
- health / control / stats snapshot を出力する
- セッション限定の一時増減を保持する

### 6.3 ThumbnailWorker.exe
- Coordinator から渡された 1ジョブを実行する
- 指定された優先度プロファイルで動く
- 結果、ログ、簡易テレメトリを返す

### 6.4 Thumbnail Engine
- Worker から呼ばれる 1ジョブ生成ライブラリとして残す
- 動画メタ取得、エンジン選択、fallback、repair、placeholder を担当する
- QueueDB ポーリング、Worker 配分、全体並列数決定は持たない

## 7. 外側の運転席で扱う項目

### 7.1 永続設定
- 希望並列数
- 巨大動画閾値
- GPU使用
- 運転モード
  - `通常優先`
  - `省電力`
  - `回復優先`

### 7.2 セッション限定設定
- 一時的に `+1 / -1`

### 7.3 表示専用
- 実効並列数
- `FastSlot` 数
- `SlowSlot` 数
- `ffmpeg.exe` 実行数 / 上限
- 通常 backlog
- 巨大動画 backlog
- 再試行 backlog
- mode 適用理由

## 8. アーキテクチャ方針

### 8.1 構成
- `IndigoMovieManager_fork.exe`
  - Coordinator launcher
  - 要約表示
- `IndigoMovieManager.Thumbnail.Coordinator.exe`
  - 運転席 UI
  - スケジューラ
  - Worker supervisor
- `IndigoMovieManager.Thumbnail.Worker.exe`
  - 実行器

### 8.2 Snapshot
- `thumbnail-progress-*.json`
  - Worker が出すジョブ進捗
- `thumbnail-health-*.json`
  - Worker / Coordinator の生死
- `thumbnail-control-*.json`
  - 外側運転席の現在設定と実効値
- `thumbnail-stats-*.json`
  - backlog と配分理由

補足:
- Coordinator は `thumbnail-progress-*.json` に Worker と同列の進捗件数を書かない
- 進捗総数は Worker progress の merge を正とし、Coordinator は control / stats / health の要約だけを持つ

### 8.3 Command
- リアルタイム IPC を無理に入れず、最初は command file 方式でよい。
- 例:
  - `thumbnail-command-<owner>.json`
- Coordinator はこの command file を監視し、反映後に `thumbnail-control-*.json` へ確定値を書く。
- command / control には `MainDbFullPath`、`OwnerInstanceId`、`VersionToken` を必須で持たせ、別DBや旧設定の誤適用を防ぐ。

## 9. キュー / スロット設計

### 9.1 キュー
- `Q1`: 通常初回
- `Q2`: 巨大動画初回
- `Q3`: 再試行 / 特殊 / `ffmpeg.exe` 対象

### 9.2 エンジン規則
- `Q1`
  - 初回 `autogen`
- `Q2`
  - 巨大動画でも初回 `autogen`
- `Q3`
  - `ffmpeg1pass` 主経路
  - `ffmpeg.exe` は失敗再処理と特殊条件だけ

補足:
- `Q1/Q2/Q3` は Coordinator が決める運転レーンであり、Engine 自身の責務ではない
- 実際の `autogen / ffmpeg1pass / ffmediatoolkit / opencv` 選択は、Worker から呼ばれる Engine 側がジョブ文脈と動画メタを見て最終決定する

### 9.3 スロット
- `FastSlot`
  - Windows優先度 `BelowNormal`
- `SlowSlot`
  - Windows優先度 `Idle`

### 9.4 取得規則
- `FastSlot`
  - まず `Q1`
  - `Q1` が空なら `Q2`
- `SlowSlot`
  - まず `Q3`
  - `Q3` が空なら `Q2`

### 9.5 詰まり防止
- `Q3` は専用枠を最低1つ持つ
- `ffmpeg.exe` は semaphore `1` を基本とする
- 通常キューが空なら `FastSlot` が巨大動画初回を拾う
- これにより、動画分布が偏っても「片側だけ遊んで他方が詰まる」構造を減らす

## 10. UI設計

### 10.1 MainWindow 側
- 要約だけを残す
- 表示項目:
  - Coordinator 接続状態
  - 実効並列数
  - 稼働 Worker 数
  - backlog 要約
- 操作:
  - 運転席を開く
  - Coordinator を再起動

### 10.2 外側運転席 UI
- 上段:
  - 希望並列数
  - 一時 `+1 / -1`
  - 巨大動画閾値
  - GPU使用
  - 運転モード
- 中段:
  - 実効並列数
  - `FastSlot`
  - `SlowSlot`
  - `ffmpeg.exe` 実行数
- 下段:
  - 通常 backlog
  - 巨大 backlog
  - 再試行 backlog
  - 適用理由
  - health

## 11. 責務境界の原則
- UI は「希望値」を送る
- Coordinator は「運転方針としての実効値」を決める
- Worker は「実行」だけを担当する
- Engine は「生成戦略」を決める
- progress / health / control は外部 snapshot を正とする
- `MainWindow` は Coordinator の内部判断を再実装しない
- Coordinator は Engine の生成規則を複製しない

## 12. データ契約

### 12.1 control snapshot
- `SchemaVersion`
- `OwnerInstanceId`
- `MainDbFullPath`
- `VersionToken`
- `RequestedParallelism`
- `TemporaryParallelismDelta`
- `EffectiveParallelism`
- `FastSlotCount`
- `SlowSlotCount`
- `GpuDecodeEnabled`
- `LargeMovieThresholdGb`
- `OperationMode`
- `Reason`
- `UpdatedAtUtc`

### 12.2 stats snapshot
- `SchemaVersion`
- `OwnerInstanceId`
- `MainDbFullPath`
- `VersionToken`
- `NormalInitialBacklog`
- `LargeInitialBacklog`
- `RetryBacklog`
- `ActiveWorkerCount`
- `ActiveFfmpegCount`
- `QueuedFfmpegCount`
- `UpdatedAtUtc`

### 12.3 command snapshot
- `SchemaVersion`
- `OwnerInstanceId`
- `MainDbFullPath`
- `VersionToken`
- `RequestedParallelism`
- `TemporaryParallelismDelta`
- `GpuDecodeEnabled`
- `LargeMovieThresholdGb`
- `OperationMode`
- `IssuedAtUtc`

## 13. 導入フェーズ

### Phase 1 外側運転席の器を作る
- 既存 `ThumbnailProgressViewer.exe` を複製せず、拡張可能かを先に評価する
- `thumbnail-control-*.json` の読取表示を追加する
- まだ制御は `MainWindow` 側のままでもよい

完了条件:
- 外側 UI で「希望値」と「実効値」を並べて見える

### Phase 2 Worker executor 化の下準備
- `ThumbnailQueueProcessor` の責務を `QueueDB リース / レーン配分` と `1ジョブ実行` に分ける
- Worker から `QueueDB を自力で読み続ける` 形を外し、`ExecuteJobAsync` 相当の入口を先に作る
- Engine 側は 1ジョブ実行の中核として残し、Queue 制御を持たせない

完了条件:
- Worker が「1ジョブ実行器」として単体起動できる
- Worker 側に QueueDB ポーリングと全体並列判断が残っていない

### Phase 3 Coordinator.exe 新設
- `ThumbnailCoordinator.exe` を新設する
- QueueDB リース、slot 配分、Worker supervisor を `MainWindow` から Coordinator へ移す
- `MainWindow` は Coordinator supervisor だけ持つ

完了条件:
- `MainWindow` が `ThumbnailWorkerProcessManager` を直接持たない
- Coordinator が唯一のスケジューラになる

### Phase 4 command file 制御
- 外側 UI から command file を更新する
- Coordinator が command を取り込み、control snapshot へ確定値を返す
- `MainDbFullPath` と `VersionToken` を見て反映対象を厳密化する

完了条件:
- 希望並列数、巨大動画閾値、GPU、mode が外側 UI から変更できる
- 旧 command や別DB command が誤反映されない

### Phase 5 slot profile 化
- `normal` / `idle` 固定 role を段階的に縮退し、slot profile ベースの worker 実行へ寄せる
- Coordinator の `FastSlot / SlowSlot` 配分と Worker 実行プロファイルを一致させる

完了条件:
- 固定 role より、slot profile ベースの実行へ寄る

## 14. 既存実装との接続点
- `ThumbnailProgressViewerWindow`
  - 外側運転席 UI の土台として使える
- `ThumbnailWorkerProcessManager`
  - 将来は Coordinator 側へ移設する
- `ThumbnailWorkerHostService`
  - 実行器化の対象
- `ThumbnailProgressExternalSnapshotStore`
  - progress merge の既存資産として利用する

## 15. リスク
- command file 方式は反映遅延が出るため、即時性の期待値を上げ過ぎないこと
- 既存 `MainWindow` と外側運転席の二重編集期間は、設定競合に注意が必要
- Worker を executor 化する前に Coordinator だけ増やすと、一時的に責務が二重化する
- Coordinator が Engine 規則まで持つと、巨大動画判定や fallback 条件が二重実装になる

## 16. 初手でやるべき最小実装
1. `thumbnail-control-*.json` 契約を定義する
2. `MainDbFullPath` と `VersionToken` を含む command/control 契約にする
3. 外側 viewer に control 表示エリアを追加する
4. `MainWindow` 側の並列数・閾値・GPU・mode を control snapshot 経由表示へ寄せる
5. その後に Worker executor 化、command file、Coordinator.exe を入れる

## 17. この計画の狙い
- 外側 UI を「見る窓」から「運転席」へ変える
- `MainWindow` の責務を軽くする
- 将来の並列配分、巨大動画偏り、retry 詰まり対策を Coordinator で一元化する
