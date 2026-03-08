# Implementation Plan: サムネイルDLL化とWindowsプライオリティ明確化（2026-03-08）

## 1. 目的
- サムネイル処理本体を UI から切り離し、Windows プライオリティを役割ごとに明確に固定できる構成へ移行する。
- UI プロセスを常に `Normal` のまま維持し、通常系とゆっくり系で異なる優先度を安全に適用する。
- 既存の `Engine.dll` / `Queue.dll` を土台にしつつ、ワーカー実行責務を DLL 化して再利用しやすくする。

## 2. 背景
- 現状は `IndigoMovieManager_fork.exe` の中でサムネイル処理を回しているため、プロセス優先度を下げると UI まで巻き込む。
- `ffmpeg.exe` だけ `Idle` に落としても、通常系ワーカー、JPEG 保存、DB 更新、UI 反映は同一プロセス内で競合する。
- さらに 1 プロセス内で通常系 `BelowNormal` とゆっくり系 `Idle` を同時に「固定」することはできない。
- そのため、Windows プライオリティを明確に分けるには、少なくとも実行ホストを役割別に分離する必要がある。

## 3. 結論
- 採るべき方針は「ワーカー責務の DLL 化 + 薄い Worker.exe ホスト化」である。
- 優先度を明確にするには、ワーカーを 1 本ではなく「通常系ホスト」と「ゆっくり系ホスト」の 2 役割で起動する。
- 既存の `src/IndigoMovieManager.Thumbnail.Engine` と `src/IndigoMovieManager.Thumbnail.Queue` はそのまま活かし、UI 依存の残りを新しい Worker DLL へ寄せる。

## 3.1 実装反映状況（2026-03-08追記）
- 完了:
  - `src/IndigoMovieManager.Thumbnail.WorkerCore` と `src/IndigoMovieManager.Thumbnail.Worker` を追加した。
  - `IndigoMovieManager_fork.sln` と `IndigoMovieManager_fork.csproj` に Worker のビルドと同梱を追加した。
  - UI 側は `Thumbnail\\Worker\\ThumbnailWorkerProcessManager.cs` で `normal` / `idle` Worker を監視起動する形へ切り替えた。
  - `src/IndigoMovieManager.Thumbnail.Queue\\ThumbnailQueueProcessor.cs` は `ThumbnailQueueWorkerRole` を受け取り、`normal` と `idle` で取得対象を分離した。
  - `src/IndigoMovieManager.Thumbnail.WorkerCore\\ThumbnailWorkerHostService.cs` で `Normal => BelowNormal`、`Idle => Idle` を固定適用する実装を入れた。
- 一部完了:
  - 進捗表示は Queue 側の状態から継続表示できるが、外部 Worker の live telemetry は簡易運用のままである。
- 保留:
  - Worker 専用の詳細状態通知と、進捗タブの完全な外部 Worker 連携は次段階とする。
- 確認結果:
  - 全テスト `230 pass / 7 skip / 0 fail`
  - MSBuild x64 Debug `0 warning / 0 error`

## 4. 到達イメージ

### 4.1 プロセス構成
- `IndigoMovieManager_fork.exe`
  - UI
  - Queue 投入
  - 設定変更
  - Worker 起動監視
- `IndigoMovieManager.Thumbnail.Worker.exe --role normal`
  - 通常系ジョブ担当
  - `autogen` 主体
  - Windows プライオリティ `BelowNormal`
- `IndigoMovieManager.Thumbnail.Worker.exe --role idle`
  - 巨大動画 / 再試行 / `ffmpeg.exe` 担当
  - Windows プライオリティ `Idle`
- 共通 DLL
  - `src/IndigoMovieManager.Thumbnail.Engine`
  - `src/IndigoMovieManager.Thumbnail.Queue`
  - `src/IndigoMovieManager.Thumbnail.WorkerCore` 新設

### 4.2 役割分担
- UI は QueueDB へ投入する Producer に徹する。
- Worker は QueueDB を読む Consumer に徹する。
- レーン判断とリース取得は共通 DLL 側へ寄せ、ホスト exe は「どの role で起動するか」だけを持つ。

## 5. DLL 化の対象

### 5.1 既に DLL 化済み
- `src/IndigoMovieManager.Thumbnail.Engine`
- `src/IndigoMovieManager.Thumbnail.Queue`

### 5.2 新たに DLL 化するもの
- `src/IndigoMovieManager.Thumbnail.WorkerCore`
  - `WorkerHostService`
  - `WorkerRole`
  - `WorkerRuntimeOptions`
  - `IWorkerPriorityController`
  - `IWorkerStatusReporter`
  - `IWorkerSettingsProvider`

### 5.3 WorkerCore へ寄せる責務
- role ごとの並列数解決
- role ごとのリース条件解決
- role ごとの Windows プライオリティ適用
- Worker 起動 / 停止 / 再起動
- QueueDB パス解決
- Worker 用ログ整形

### 5.4 UI に残す責務
- MainDB オープン / 切替
- Queue 投入
- タブ表示
- 設定編集
- Worker 生死監視と再起動要求

## 6. Windows プライオリティ方針

### 6.1 固定ルール
- UI プロセス:
  - 常に `Normal`
  - サムネイル処理開始 / 終了で変更しない
- 通常系 Worker:
  - 常に `BelowNormal`
  - 初回 `autogen` と軽い通常処理を担当
- ゆっくり系 Worker:
  - 常に `Idle`
  - 巨大動画、再試行、`ffmpeg.exe` を担当
- `ffmpeg.exe`:
  - ゆっくり系 Worker からのみ起動
  - 常に `Idle`

### 6.2 明確化したい理由
- 「ジョブの種類」と「Windows の譲り方」を 1 対 1 で対応させるため。
- 実行中に優先度を揺らす設計だと、ログも挙動説明も曖昧になるため。
- role 単位の固定にすると、ユーザー説明、ログ、ベンチ比較が全部簡単になるため。

### 6.3 実装ルール
- `ProcessPriorityClass` は起動直後に 1 回適用する。
- `Idle` Worker は可能なら Windows の background mode も併用候補とする。
- UI スレッドへは優先度変更をかけない。
- 通常系とゆっくり系を同一プロセスへ混在させない。

## 7. role ごとの実行ポリシー

| role | 担当 | 取得条件 | 並列 | 先読み | Windows優先度 |
|---|---|---|---|---|---|
| `normal` | 初回通常処理 | `AttemptCount = 0` かつ 巨大動画閾値未満中心 | 設定並列の主枠 | 4件 | `BelowNormal` |
| `idle` | 巨大動画 / 再試行 | `AttemptCount > 0` または 巨大動画閾値以上 | 1本中心 | 1件 | `Idle` |

補足:
- `normal` が空いていても、`ffmpeg.exe` は `normal` 側で実行しない。
- 巨大動画の通常系代行は、Phase 1 では切る。優先度の明確化を優先する。

## 8. 新規構成案

### 8.1 新規プロジェクト
- `src/IndigoMovieManager.Thumbnail.WorkerCore/IndigoMovieManager.Thumbnail.WorkerCore.csproj`
  - `net8.0-windows`
  - `OutputType=Library`
  - `ProjectReference`
    - `src/IndigoMovieManager.Thumbnail.Engine`
    - `src/IndigoMovieManager.Thumbnail.Queue`
- `src/IndigoMovieManager.Thumbnail.Worker/IndigoMovieManager.Thumbnail.Worker.csproj`
  - `net8.0-windows`
  - `OutputType=Exe`
  - `ProjectReference`
    - `src/IndigoMovieManager.Thumbnail.WorkerCore`

### 8.2 Worker.exe の役割
- 引数を受けるだけの薄いホストにする。
- 例:
  - `IndigoMovieManager.Thumbnail.Worker.exe --role normal --main-db "..."`
  - `IndigoMovieManager.Thumbnail.Worker.exe --role idle --main-db "..."`
- 実処理は `WorkerCore.dll` の `WorkerHostService` へ集約する。

## 9. 既存コードの主な移設先

### 9.1 移設候補
- `MainWindow.xaml.cs` に残る Worker 起動相当の責務
- `Thumbnail/MainWindow.ThumbnailCreation.cs` の常駐処理開始責務
- Worker 向けログ構築
- Worker 向け設定スナップショット組み立て

### 9.2 そのまま使う
- `ThumbnailQueueProcessor`
- `QueueDbService`
- `ThumbnailCreationService`
- `ThumbnailEngineRouter`
- `FfmpegOnePassThumbnailGenerationEngine`

### 9.3 小修正が必要
- `ThumbnailQueueProcessor`
  - role ごとの取得ポリシーを受け取れるようにする
- `ThumbnailLaneClassifier`
  - role 判定前提の補助用途へ整理する
- `MainWindow.xaml.cs`
  - 直接 `RunAsync` を回さず、Worker 起動監視に差し替える

## 10. 実装ステップ

### Phase 1: WorkerCore.dll を作る
- `WorkerRole` と `WorkerRuntimeOptions` を追加する。
- Worker 用 priority controller を追加する。
- `ThumbnailQueueProcessor` を role 指定で起動できる薄いラッパーを作る。

完了条件:
- UI 非依存で `normal` / `idle` role を起動できる。

状況:
- 完了

### Phase 2: Worker.exe を作る
- `Program.cs` で引数解析を実装する。
- `WorkerHostService.RunAsync(...)` を呼ぶ。
- 起動直後に role ごとの Windows プライオリティを固定適用する。

完了条件:
- Worker 単体起動で QueueDB を処理できる。
- role ごとに優先度ログが出る。

状況:
- 完了

### Phase 3: UI から 2 役割 Worker を管理する
- MainDB オープン時に `normal` と `idle` の 2 Worker を起動する。
- DB 切替 / 終了時に両方止める。
- 二重起動防止を入れる。

完了条件:
- UI は `Normal` のまま。
- `normal` Worker は `BelowNormal`、`idle` Worker は `Idle` で動く。

状況:
- 完了
- Worker が見つからない環境では旧 in-process consumer へフォールバックする安全弁を残している。

### Phase 4: 進捗表示を縮退再接続する
- Phase 1 では live パネルを簡易表示へ縮退する。
- Worker 状態は QueueDB 補助情報か定期スナップショットで返す。

完了条件:
- 進捗タブで件数と role 状態が分かる。

状況:
- 一部完了
- `ゆっくり / 再試行専 / 通常 n` の表示は更新済みだが、外部 Worker 個別の詳細状態通知は未実装である。

### Phase 5: 旧 in-process 常駐処理を除去する
- UI 内で直接サムネイル処理を回す経路を閉じる。
- `IMM_THUMB_PROCESS_PRIORITY` 依存の旧挙動を削る。

完了条件:
- 優先度制御が Worker 側へ一本化される。

状況:
- 一部完了
- 通常運用は外部 Worker 前提へ切り替え済みだが、フォールバック経路はまだ残している。

## 11. 影響ファイル
- `IndigoMovieManager_fork.csproj`
- `IndigoMovieManager_fork.sln`
- `MainWindow.xaml.cs`
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/MainWindow.ThumbnailQueue.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
- `Thumbnail/Engines/FfmpegOnePassThumbnailGenerationEngine.cs`
- `src/IndigoMovieManager.Thumbnail.WorkerCore/*` 新規
- `src/IndigoMovieManager.Thumbnail.Worker/*` 新規

## 12. リスク
- 進捗タブの live 表示は一時的に簡略化が必要。
- Worker 二重起動を防がないと Queue リース競合が起こる。
- MainDB 切替時の Worker 差し替えを誤ると別 DB を処理する。
- `MovieInfo` 依存や UI 文脈ログが Worker 側でそのまま使えない可能性がある。

## 13. 今回の実装判断ルール
- Windows プライオリティを明確にしたいなら、1 プロセスへ役割を混ぜない。
- `Idle` を使うなら UI と同居させない。
- `ffmpeg.exe` を通常系へ戻さない。
- 優先度変更は「実行中だけ上下させる」より「role 固定」を優先する。

## 14. 完了条件
- UI プロセスの `ProcessPriorityClass` を変更しない。
- `normal` Worker が `BelowNormal` 固定で動く。
- `idle` Worker が `Idle` 固定で動く。
- `ffmpeg.exe` は `idle` Worker 配下でのみ動く。
- role ごとのログに、`role / priority / mainDbPath / ownerInstanceId` が残る。
- 旧 in-process 優先度変更経路が除去される。

状況:
- 2026-03-08 時点で本体 `Build` は成功。
- `IndigoMovieManager_fork_*_wpftmp.csproj` の重複属性問題は解消済み。
- 本体 `Build` は `0 warning / 0 error`。
- テスト全体は `255 pass / 7 skip / 0 fail`。

## 15. 次アクション
1. 外部 Worker 向けの live telemetry を追加し、進捗タブの状態粒度を戻す。
2. フォールバック前提の旧 in-process consumer を段階的に縮退させる。
3. role ごとの状態ログをユーザー向けに見やすく整理する。
4. 実機で `BelowNormal` / `Idle` の体感差と干渉低減を確認する。
