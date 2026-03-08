# Implementation Plan: 専用ワーカープロセス分離_最小構成_2026-03-07

## 1. 目的

- サムネイル生成中だけアプリ全体の `ProcessPriorityClass` を下げる現状から脱却する。
- UIプロセスとサムネイル生成プロセスを分離し、Windows優先度をワーカー側だけへ適用できるようにする。
- 既存の `QueueDB` 中心アーキテクチャを活かし、最小変更で `Worker.exe` を成立させる。

## 2. 結論

- 可能。
- しかも今の repo は、すでに `Engine.dll` と `Queue.dll` まで分かれているため、土台はかなり良い。
- 最小構成では `IPCでジョブ投入` はやらず、今の `QueueDB` をそのままジョブ受け渡し面として使う。
- 先に `Worker.exe` を生やし、進捗の高機能表示は第2段階へ回すのが安全。

## 3. 最小構成のアーキテクチャ

### 3.1 役割分担

- `IndigoMovieManager_fork.exe`
  - UI
  - Watcher / 手動操作
  - `ThumbnailQueuePersister` による QueueDB への投入
  - Queue件数表示、失敗一覧表示
- `IndigoMovieManager.Thumbnail.Worker.exe`
  - QueueDB からのリース取得
  - `ThumbnailQueueProcessor.RunAsync(...)` 実行
  - `ThumbnailCreationService.CreateThumbAsync(...)` 実行
  - Windows優先度 `Idle / BelowNormal` の適用
- 共通DLL
  - `src/IndigoMovieManager.Thumbnail.Engine`
  - `src/IndigoMovieManager.Thumbnail.Queue`

### 3.2 最初はやらないこと

- ワーカー内 live プレビューの UI 反映
- Named Pipe によるジョブ投入
- UI と Worker 間の双方向リアルタイム同期
- Worker 複数起動制御

## 4. 今の構成で使えるもの

### 4.1 そのまま流用できる

- `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj`
  - サムネイル生成本体はすでに DLL 化済み
- `src/IndigoMovieManager.Thumbnail.Queue/IndigoMovieManager.Thumbnail.Queue.csproj`
  - QueueProcessor / QueueDb / QueuePipeline は DLL 化済み
- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
  - QueueDB をワーカーとの共有面として使える
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
  - リース、ハートビート、完了/失敗更新、レーン制御がまとまっている
- `src/IndigoMovieManager.Thumbnail.Queue/QueuePipeline/ThumbnailQueuePersister.cs`
  - UI 側 Producer としてそのまま残せる

### 4.2 まだ本体依存が残る

- `Thumbnail/Adapters/AppVideoMetadataProvider.cs`
  - `MovieInfo` に依存
- `Thumbnail/Adapters/AppThumbnailLogger.cs`
  - `DebugRuntimeLog` に依存
- `Thumbnail/Adapters/AppThumbnailQueueProgressPresenter.cs`
  - `Notification.Wpf` に依存
- `MainWindow.xaml.cs`
  - `ThumbnailProgressRuntime` と UI 直結

## 5. 最小構成の設計方針

### 5.1 ジョブ投入は QueueDB のまま

- UI は今まで通り `QueueRequest` を `ThumbnailQueuePersister` へ流す。
- `Worker.exe` は `QueueDbService` を直接開いて `RunAsync(...)` を回す。
- これで「投入面の IPC 新設」を先送りできる。

### 5.2 Worker は MainDB パスを受けて起動する

- 例:
  - `IndigoMovieManager.Thumbnail.Worker.exe --main-db "C:\\path\\main.wb" --owner INSTANCE_A`
- Worker は `QueueDbService(mainDbPath)` を生成してコンシュームする。
- MainDB 切替時は UI 側が旧 Worker を落とし、新 Worker を起動する。

### 5.3 進捗表示は段階的に落とす

- 第1段階:
  - UI は Queue 件数と失敗一覧中心
  - live worker パネルは無効化または簡易表示
- 第2段階:
  - Worker から QueueDB 補助テーブル or IPC で進捗スナップショットを送る

### 5.4 優先度は Worker.exe 側だけに適用する

- ここで初めて `ProcessPriorityClass.Idle` が筋の良い実装になる。
- UI プロセスは常に `Normal` のままにできる。

## 6. ファイル単位の最小変更案

### 6.1 新規追加

- `src/IndigoMovieManager.Thumbnail.Worker/IndigoMovieManager.Thumbnail.Worker.csproj`
  - `net8.0-windows`
  - `OutputType=Exe`
  - `PlatformTarget=x64`
  - `ProjectReference`
    - `src/IndigoMovieManager.Thumbnail.Engine`
    - `src/IndigoMovieManager.Thumbnail.Queue`
- `src/IndigoMovieManager.Thumbnail.Worker/Program.cs`
  - 引数解析
  - Worker 起動
  - Ctrl+C / 終了シグナル処理
- `src/IndigoMovieManager.Thumbnail.Worker/WorkerHost.cs`
  - `QueueDbService` 生成
  - `ThumbnailQueueProcessor.RunAsync(...)` 呼び出し
- `src/IndigoMovieManager.Thumbnail.Worker/WorkerThumbnailLogger.cs`
  - `IThumbnailLogger` 実装
  - Worker 専用ログ出力
- `src/IndigoMovieManager.Thumbnail.Worker/WorkerVideoMetadataProvider.cs`
  - `IVideoMetadataProvider` 実装
  - 最初は `MovieInfo` を参照するか、必要なら Engine 側へ移設

### 6.2 既存修正

- `IndigoMovieManager_fork.sln`
  - Worker プロジェクト追加
- `MainWindow.xaml.cs`
  - `ThumbnailQueueProcessor` の常駐起動をやめる
  - Worker 起動/停止監視へ差し替える
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - `CheckThumbAsync()` を Worker 管理に置換
- `Thumbnail/MainWindow.ThumbnailQueue.cs`
  - QueueDB 切替時に Worker 再起動
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
  - UI 前提の `progressPresenter` を完全に optional のまま維持
  - Worker 運用では `NoOp` を使う
- `Thumbnail/Adapters/AppVideoMetadataProvider.cs`
  - Worker からも使える置き場所へ移すか、Worker 用実装を別作成
- `Thumbnail/Adapters/AppThumbnailLogger.cs`
  - Worker 直参照は避け、Worker 側専用実装へ分離

## 7. 実装順

### Phase 1: Worker.exe を成立させる

- `Worker.csproj` 追加
- `Program.cs` 追加
- MainDB パスを受けて `ThumbnailQueueProcessor.RunAsync(...)` を起動
- `NoOpThumbnailQueueProgressPresenter` で UI 通知なし運転
- Worker 側へ `Idle / BelowNormal` を適用

完了条件:

- Worker 単体起動で QueueDB の Pending を処理できる
- UI を閉じても Worker がジョブを処理できる
- UI プロセスへ `Idle` をかけなくて済む

### Phase 2: UI から Worker を管理する

- MainDB オープン時に Worker 起動
- DB切替 / 終了時に Worker 停止
- 二重起動防止

完了条件:

- UI から通常操作した投入ジョブが Worker 側で消化される
- MainDB 切替時に別DBへ誤接続しない

### Phase 3: 進捗の再接続

- Worker の状態を UI へ返す最小経路を追加
- 候補:
  - QueueDB 補助テーブル
  - Named Pipe
  - 一時 JSON スナップショット

完了条件:

- 進捗タブの `現在処理中件数 / 実効並列 / 代表ファイル名` 程度が戻る

## 8. 最小構成で切り捨てる仕様

- Worker パネルの画像プレビュー
- `ThumbnailProgressRuntime` の full fidelity 再現
- UI 内 Notification.Wpf と同等のリアルタイム通知

理由:

- ここを最初から追うと、分離コストの大半を UI 再同期が食う
- まずは「処理を別プロセスへ出す」が本命

## 9. 技術的な注意点

### 9.1 `MovieInfo` 依存

- `AppVideoMetadataProvider` は現状 `MovieInfo` を使う。
- Worker でも同じ参照が通るなら流用可能。
- ただし将来的には `IVideoMetadataProvider` を Engine 側で自給できる形へ寄せた方がきれい。

### 9.2 ログ出力

- `DebugRuntimeLog` は UI アプリ文脈が強い。
- Worker 側は
  - 同じログフォルダへ別カテゴリで書く
  - もしくは `worker-runtime.log` を持つ
- のどちらかへ分けた方が追いやすい。

### 9.3 QueueDB 所有者ID

- Worker ごとに `ownerInstanceId` を持たせる。
- UI プロセスと Worker プロセスで所有者を混ぜない。
- 既存の `UpdateStatus(... ownerInstanceId ...)` はこの点で相性が良い。

### 9.4 QueueDB パス解決

- MainDB パスは UI と Worker で一致している必要がある。
- `QueueDbPathResolver.ResolveQueueDbPath(mainDbPath)` を共通利用する。

## 10. 推奨する最初の完成ライン

- Worker は1本だけ
- ジョブ投入は QueueDB のまま
- 進捗タブは件数中心に一時縮退
- 優先度は Worker.exe だけ `Idle`

このラインなら、実装コストを抑えつつ「UIが重くならない」という本命の効果を先に取れる。

## 11. 今の repo 前提での判断

- いま着手するなら、`ThreadPriority` を詰めるより `Worker.exe` 分離の方が将来性が高い。
- ただし最小構成では、進捗タブの豪華表示を一時的に捨てる割り切りが必要。
- 先に欲しいのが「UI干渉を消すこと」なら、この割り切りは妥当。

## 12. 次アクション

1. `Worker.exe` 新規プロジェクトを追加する
2. `Program.cs` で MainDB 指定起動を実装する
3. `MainWindow` の `CheckThumbAsync()` を Worker 管理へ置換する
4. 進捗タブは Phase 1 では件数中心へ縮退する

