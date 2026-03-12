# Implementation Plan + tasklist（Queue実行状態分離とHangSuspected, 2026-03-10）

## 0. 目的
- `Processing` 1状態に lease 済み未着手ジョブと実行中ジョブが混在する問題を解消する。
- 難動画で「返ってこない失敗」を `HangSuspected` として切り出し、通常再試行と別経路で扱えるようにする。
- `worker / coordinator / QueueDB / UI` の観測点を揃え、停滞原因をログと snapshot から追える状態にする。
- `サムネ失敗` タブは QueueDB の最終失敗一覧ではなく、サムネ失敗専用DBを正として表示する。
- DebugMode ではサムネ失敗の全件を専用DBへ insert し、失敗タブと調査の基礎データにする。

## 1. 背景と現状
- 実機追跡では `worker / coordinator` は生存し、`thumbnail-health-*.json` も更新され続けていた。
- 一方で QueueDB は `Pending=0`, `Processing=13`, `Done=2` のまま長時間変化せず、古い `Processing` 行の `LeaseUntilUtc` / `UpdatedAtUtc` も進まないケースがあった。
- `ffmpeg.exe` は追跡時に存在せず、単純な外部子プロセスぶら下がりではなかった。
- 主因は「難動画で処理が長引く中、実行枠以上の lease 先取りで `Processing` が膨らむこと」と判断した。
- これに対して 2026-03-10 に `LeaseBatchSize` を実行枠上限へ抑える修正を反映済み。
- 別AIエージェント側で `FailureDb` の path resolver / schema / service / record DTO は実装済み。
- `AutogenRepairPlaygroundTests` でも `FailureDb` への append 記録は実装済みで、`autogen` 試行失敗は専用DBへ残せる。
- 本体側の残りは「Queue 本番経路の insert 接続」と「サムネ失敗タブの専用DB表示切替」である。

## 2. 実装方針
- まず `lease` 先取りを抑えた現状を基準線とし、その上で QueueDB 状態を細分化する。
- `Processing` を増やすのではなく、`Leased` と `Running` を明示的に分ける。
- `HangSuspected` は engine 例外文字列分類ではなく、Queue / runtime の時間監視から補助判定する。
- 停滞検知は「プロセス生存」と切り離し、「実処理が進んでいるか」で判断する。
- UI は Queue の意味を再解釈せず、Queue / coordinator が出した状態をそのまま表示する。

## 3. 対象
- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbSchema.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailProgressRuntime.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailProgressExternalSnapshotStore.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailCoordinatorControlSnapshot.cs`
- `src/IndigoMovieManager.Thumbnail.Coordinator/ThumbnailCoordinatorHostService.cs`
- `MainWindow.xaml.cs`
- `MainWindow.xaml`
- `ModelViews/ThumbnailProgressViewState.cs`
- `src/IndigoMovieManager.Thumbnail.ProgressViewer/*`
- `Tests/IndigoMovieManager_fork.Tests/*`
- `scripts/trace_thumbnail_process_tree.ps1`
- `scripts/trace_thumbnail_runtime.py`
- サムネ失敗専用DB 用の新規 service / store / DTO

## 4. 対象外
- MainDB のスキーマ変更
- サムネイル生成 engine 本体の全面刷新
- Watcher の再投入判定ロジック全面変更
- `ffmpeg.exe` 実行戦略の再設計

## 5. 詳細設計

### 5.1 QueueDB 状態の分離
- 既存の `Pending / Processing / Done / Failed` を見直し、少なくとも内部意味として以下を分ける。
  - `Pending`
  - `Leased`
  - `Running`
  - `Done`
  - `Failed`
- 初期段階では DB 列挙値を追加するか、`Status` は維持しつつ `StartedAtUtc` で擬似分離するかを実装時に選ぶ。
- ただし UI / ログ / 調査スクリプトから `Leased` と `Running` が識別できることを受け入れ条件にする。

### 5.2 実行遷移
- Queue 取得時は `Pending -> Leased`
- 実際に engine 実行へ入る直前で `Leased -> Running`
- 成功時は `Running -> Done`
- 通常失敗時は `Running -> Pending` または `Running -> Failed`
- 停止キャンセル時は `Leased/Running -> Pending`
- DB 切替や owner 不一致時は `Leased/Running -> Pending`

### 5.3 heartbeat と時間監視
- heartbeat は `Running` ジョブだけ延長対象にする。
- `Leased` は「未着手」のため heartbeat を持たない。
- `Running` には以下の時刻を持たせる。
  - `LeaseUntilUtc`
  - `StartedAtUtc`
  - `LastHeartbeatUtc`
  - `UpdatedAtUtc`
- 停滞判定は `StartedAtUtc` 基準の実行時間と heartbeat 更新の両方で行う。

### 5.4 HangSuspected
- `HangSuspected` は `FailureKind` の一種として扱う。
- 初回判定条件案:
  - `Running` が最大実行時間を超過
  - heartbeat が生きていても `UpdatedAtUtc` や進捗が長時間変化しない
  - child process が存在しなくても engine 実行が返ってこない
- 回復方針案:
  - 1回目は `Pending` へ戻し隔離レーン送り
  - 2回目以降は `Failed` へ寄せるか、手動確認推奨フラグを立てる

### 5.5 表示と観測
- `ThumbnailProgressRuntimeSnapshot` へ追加候補:
  - `LeasedCount`
  - `RunningCount`
  - `HangSuspectedCount`
- `ThumbnailCoordinatorControlSnapshot` へ追加候補:
  - `QueuedLeasedCount`
  - `QueuedRunningCount`
  - `HangSuspectedRecentCount`
  - `LastHangSuspectedAtUtc`
- `MainWindow` と外側運転席は、少なくとも `Leased / Running / HangSuspected` を要約表示する。
- 調査スクリプトも新状態を読み、現状の `Processing` 集計から切り替える。

### 5.6 サムネ失敗タブと専用DB
- `サムネ失敗` タブ active 時は、QueueDB ではなくサムネ失敗専用DBを読む。
- 専用DBは少なくとも以下を持つ。
  - `DbName`
  - `MoviePath`
  - `PanelType`
  - `MovieSizeBytes`
  - `Duration`
  - `Reason`
  - `FailureKind`
  - `AttemptCount`
  - `OwnerInstanceId`
  - `OccurredAtUtc`
  - `UpdatedAtUtc`
- 追加候補:
  - `TabIndex`
  - `EngineId`
  - `WorkerRole`
  - `QueueStatus`
  - `DebugContextJson`
- 専用DBは「最終失敗一覧」ではなく、「失敗調査用の履歴DB」として扱う。
- 失敗タブは active 時だけ再読込し、非 active 時は dirty フラグだけ立てる。

### 5.7 DebugMode 失敗DB insert
- DebugMode 時は、サムネ失敗のすべてのケースで専用DBへ insert する。
- 対象は最終失敗だけでなく、途中失敗、再試行戻し、`HangSuspected`、placeholder 分岐前後も含める。
- 最低限保持する列:
  - `DbName`
  - `MoviePath`
  - `PanelType`
  - `MovieSizeBytes`
  - `Duration`
  - `Reason`
  - `FailureKind`
  - `AttemptCount`
- 追加で保持したい列:
  - `EngineId`
  - `WorkerRole`
  - `OwnerInstanceId`
  - `LeaseUntilUtc`
  - `StartedAtUtc`
  - `UpdatedAtUtc`
  - `LastError`
  - `ExtraJson`
- insert は debug 専用にし、通常運用では抑止できるよう設定で分離する。

### 5.8 互換と段階導入
- 第1段階では `LeaseBatchSize` 抑制を維持しつつ、状態分離だけを追加する。
- 第2段階で DebugMode 専用DB insert を追加する。
- 第3段階で `HangSuspected` を回復経路へ接続する。
- 第4段階で `サムネ失敗` タブを専用DB 表示へ切り替える。
- 第5段階で UI と運転席へ停滞表示を追加する。
- 第6段階で古い `Processing` 前提ログや表示文言を整理する。

## 6. 受け入れ条件
- `Leased` と `Running` を QueueDB と snapshot から区別できる。
- 難動画で停滞した時、`Pending=0 / Processing大量` ではなく `Leased` と `Running` の内訳で把握できる。
- `HangSuspected` は `Unknown` や `TransientDecodeFailure` に埋もれない。
- `trace_thumbnail_runtime.py` が新状態を時系列採取できる。
- `MainWindow` または外側運転席で停滞件数を要約表示できる。
- `サムネ失敗` タブ active 時は、サムネ失敗専用DBの内容を表示できる。
- DebugMode では失敗の全件が専用DBへ insert され、`FailureKind` と `Reason` を追える。

## 7. タスクリスト

| ID | 状態 | タスク | 対象 | 完了条件 |
|---|---|---|---|---|
| QH-001 | 完了 | `LeaseBatchSize` を実行枠上限へ抑制 | `ThumbnailWorkerSettingsResolver.cs`, `ThumbnailQueueProcessor.cs` | 実行枠以上の先取り lease をしない |
| QH-002 | 完了 | 実機追跡スクリプトを追加 | `scripts/trace_thumbnail_process_tree.ps1`, `scripts/trace_thumbnail_runtime.py` | process / health / control / QueueDB を同時採取できる |
| QH-003 | 完了 | 実測結果をドキュメント化 | `調査結果_サムネイルProcessing残留とlease先取り過多_2026-03-10.md` | 原因仮説と対策基準線が残る |
| QH-004 | 完了 | QueueDB 状態分離方針を確定 | `QueueDbSchema.cs`, `QueueDbService.cs`, 設計メモ | `StartedAtUtc` による `Leased / Running` 擬似分離を採用 |
| QH-005 | 完了 | `Pending -> Leased -> Running` 遷移を実装 | `QueueDbService.cs`, `ThumbnailQueueProcessor.cs` | 実行前に `MarkLeaseAsRunning(...)` で `Running` へ上がる |
| QH-006 | 完了 | heartbeat を `Running` 限定へ変更 | `ThumbnailQueueProcessor.cs`, `QueueDbService.cs` | `Leased` 行が heartbeat で延命されない |
| QH-007 | 完了 | `StartedAtUtc` と停滞監視を追加 | `QueueDbService.cs`, `ThumbnailQueueProcessor.cs` | `StartedAtUtc` が入り、停滞判定の基礎列が揃う |
| QH-008 | 完了 | `HangSuspected` を `FailureKind` へ追加 | `FailureKind` 周辺, policy, finalizer | `TimeoutException` などを `HangSuspected` として保持できる |
| QH-009 | 完了 | `HangSuspected` の状態遷移と回復方針を接続 | `ThumbnailQueueProcessor.cs`, policy | 初回は recovery レーンへ戻し、recovery 再発は `Failed` へ落とせる |
| QH-010 | 完了 | progress snapshot に `Leased / Running / HangSuspected` を追加 | `ThumbnailProgressRuntime.cs`, `ThumbnailProgressExternalSnapshotStore.cs` | worker publish / merged snapshot / 本体 / 運転席で totals を見える |
| QH-011 | 完了 | coordinator control に停滞要約を追加 | `ThumbnailCoordinatorControlSnapshot.cs`, `ThumbnailCoordinatorHostService.cs` | control snapshot に `queued / leased / running / hang` が載り、運転席要約へ反映される |
| QH-012 | 完了 | 本体サムネイルタブへ停滞要約を追加 | `MainWindow.xaml.cs`, `ThumbnailProgressViewState.cs` | `leased / running / hang` を本体サムネイルタブで確認できる |
| QH-013 | 完了 | 外側運転席へ停滞表示を追加 | `ThumbnailProgressViewerWindow.xaml`, `.xaml.cs` | 運転席で `queued / leased / running / hang` を確認できる |
| QH-014 | 完了 | 調査スクリプトを新状態対応に更新 | `trace_thumbnail_runtime.py` | `Processing` 前提をやめて `queued / leased / running / hang` を表示できる |
| QH-015 | 完了 | Queue 回帰テストを追加 | `Tests/IndigoMovieManager_fork.Tests/*` | 状態遷移と停滞回復を自動検証できる |
| QH-016 | 完了 | 手動確認手順を追加 | `Thumbnail/*Manual*` or 新規 md | 難動画停滞時の確認フローが残る |
| QH-017 | 完了 | DebugMode 失敗専用DB schema / service を追加 | `FailureDb/*` | path / schema / service / record DTO が揃い、単体テストが通る |
| QH-018 | 完了 | DebugMode 失敗時 insert を全経路へ接続 | `ThumbnailQueueProcessor.cs`, finalizer, policy 周辺 | 途中失敗も最終失敗も専用DBへ残り、`FailureDb.ExtraJson` で policy / placeholder / finalizer の判断痕跡を追える |
| QH-019 | 完了 | `サムネ失敗` タブの専用DB 表示切替を実装 | `MainWindow.xaml`, `MainWindow.xaml.cs` | active 時は専用DB表示になる |
| QH-020 | 完了 | 専用DB 表示用 ViewModel / テストを追加 | `ModelViews/*`, `Tests/*` | 失敗タブの列と読込条件を検証できる |

## 8. 実装順

### Phase A 基盤
- QH-004
- QH-005
- QH-006
- QH-007

### Phase B 分類
- QH-018
- QH-009

### Phase C 観測
- QH-019
- QH-020
- QH-010
- QH-011
- QH-012
- QH-013
- QH-014

### Phase D 固定
- QH-015
- QH-016

## 9. リスクと対策
- リスク:
  - 状態追加で QueueDB 互換を崩す
  - 対策:
    - まずは既存 DB で読める追加列または互換 enum 拡張で始める
- リスク:
  - `HangSuspected` が誤検知で増える
  - 対策:
    - 1回目は隔離再投入優先にし、即 `Failed` 固定しない
- リスク:
  - UI に状態が増えて読みにくくなる
  - 対策:
    - 本体は要約表示、詳細は外側運転席へ寄せる
- リスク:
  - 調査スクリプトと snapshot スキーマがずれる
  - 対策:
    - `SchemaVersion` と optional 読み取りで後方互換を維持する

## 10. 確認元
- `Thumbnail/調査結果_サムネイルProcessing残留とlease先取り過多_2026-03-10.md`
- `Thumbnail/Implementation Plan_サムネイルWorker完全責務移譲_長期計画_2026-03-08.md`
- `Thumbnail/設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md`
- `Thumbnail/連絡用_サムネ失敗専用DB先行実装_2026-03-10.md`
- `Thumbnail/連絡用_サムネ失敗専用DB先行実装_完了連絡_2026-03-10.md`
- `Thumbnail/Implementation Plan_サムネ失敗専用DB先行実装とautogen試験ハーネスDB記録_2026-03-10.md`
- `scripts/trace_thumbnail_process_tree.ps1`
- `scripts/trace_thumbnail_runtime.py`

## 11. 2026-03-10 完了連絡反映
- `FailureDb` 土台は別AIエージェント側で完了済みとみなす。
- 本計画で引き取る後続は以下の2本に絞る。
  - `QH-018`: Queue 本番経路の DebugMode insert 接続
  - `QH-019` / `QH-020`: `サムネ失敗` タブの専用DB表示切替
- `AutogenRepairPlaygroundTests` での `FailureDb` 利用は実装済みのため、本体側では同じ service 契約を流用して差し込む。

## 12. 2026-03-10 本体側進行メモ
- `HandleFailedItem`
  - `retry-scheduled`
  - `final-failed`
  - `db-scope-changed`
  の 3 経路は `FailureDb` へ append 済み。
- `HandleCanceledItem` も `reason=canceled` で `FailureDb` へ append 済み。
- `HangSuspected` は `TimeoutException` と timeout 文言から `FailureKind` へ反映済み。
- `HangSuspected` の状態遷移も接続済み。
  - 初回の `HangSuspected` は `hang-recovery-scheduled` として `Pending + IsRescueRequest=1` へ戻す。

## 13. 2026-03-11 FailureDb.ExtraJson 先行反映メモ
- workthree 受け皿として、`FailureDb.ExtraJson` の `A` キーを本線へ先行反映済み。
  - `result_signature`
  - `recovery_route`
  - `decision_basis`
  - `repair_attempted`
  - `preflight_branch`
- 実装位置:
  - `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- 表示側の受け皿:
  - `Thumbnail/MainWindow.ThumbnailFailedTab.cs`
  - `Tests/IndigoMovieManager_fork.Tests/ThumbnailFailedTabViewModelTests.cs`
  - `snake_case` の workthree 最小 fixture を失敗タブ ViewModel へ復元できる前提まで固定
- 検証:
  - `ThumbnailQueueProcessorFailureDbTests` 合格
- この段階では「救済ロジック導入」ではなく、「比較と受領のための器」を固定した位置付けとする。
  - 既に recovery レーン上の `HangSuspected` 再発は通常 retry へ戻さず `final-failed` へ落とす。
- progress snapshot には `LeasedCount / RunningCount / HangSuspectedCount` を追加済み。
  - worker 側 publish 時に QueueDB から観測値を引き、外部 snapshot と merged snapshot へ流す。
  - 本体サムネイルタブと外側運転席の基本情報へ `leased / running / hang` 行を追加済み。
- `ThumbnailCreateFailedException` を導入し、UI / Worker から Queue 層へ失敗 result 本体を渡す構成へ変更済み。
- `FailureDb.ExtraJson` には以下の queue 観測項目を載せる実装が完了。
  - `MainDbFullPath`
  - `AttemptCountAfter`
  - `NextStatus`
  - `FailureKindSource=queue`
  - `WasRunning`
  - `MovieExists`
  - `LeaseUntilUtc`
  - `StartedAtUtc`
  - `ExceptionType`
  - `ExceptionMessage`
- `FailureDb.ExtraJson` には以下の result 観測項目も載せる実装が完了。
  - `ResultErrorMessage`
  - `ResultFailureStage`
  - `ResultPolicyDecision`
  - `ResultPlaceholderAction`
  - `ResultPlaceholderKind`
  - `ResultFinalizerAction`
  - `ResultFinalizerDetail`
- `サムネ失敗` タブでは以下の列を表示済み。
  - `AttemptAfter`
  - `MovieExists`
  - `ResultStage`
  - `PolicyDecision`
  - `PlaceholderAction`
  - `PlaceholderKind`
  - `FinalizerAction`
  - `FinalizerDetail`
- `ThumbnailFailedTabViewModelTests` で、`FailureDb.ExtraJson -> ThumbnailFailedRecordViewModel` 変換を固定済み。
- `Tests/IndigoMovieManager_fork.Tests` のビルドと、`ThumbnailQueueProcessorFailureDbTests` / `ThumbnailFailedTabViewModelTests` の対象テスト確認まで完了。

## 14. 2026-03-11 workthree 優先順位表受領メモ
- workthree 側で、失敗 9 件の検証順が整理された。
- 本線側では、次の順で知見受領と反映判断を行う。

受領優先:
- `P1`
  - `near-black` 5件グループ
  - `画像1枚あり顔.mkv`
  - `画像1枚ありページ.mkv`
- `P2`
  - `ライブ配信真空エラー2_ghq5_temp.mp4`
  - `OTD-093-2-4K.mp4`
  - `ラ・ラ・ランド 1/2, 2/2`
- `P3`
  - `【ライブ配信】神回...`
  - `映像なし_scale_2x_prob-3...mkv`
  - `_steph_myers_...mp4`

本線側で先に決めたこと:
- `P1` は `FailureDb.ExtraJson` の比較と `FailureKind` 境界整理を優先する
- `near-black` 群は補助分類または補助属性候補として扱う
- `画像1枚あり*` は `ShortClipStillLike` と `ManualCaptureRequired` の境界確認対象とする
- `P2` は `PhysicalCorruption / ContainerMetadataBroken / TransientDecodeFailure` のどこへ寄せるかを重点判断にする
- `P3` は「救済不能を明確化する」結果も受け入れる

workthree 側の直近作業予定:
- `near-black` 5件を掘るための比較ハーネスを持ち込む
- `画像1枚あり顔.mkv` と `画像1枚ありページ.mkv` の再現ハーネスを持ち込む
- その結果を優先順位表へ追記する

## 15. 2026-03-11 本線継続メモ
- `IndigoMovieManager_fork.csproj` に `**\artifacts\**\*` 除外を追加した。
  - `src/*/artifacts/msbuild-verify/*` の生成物が本体プロジェクトの既定 item へ混入し、`commonsettingswindow.baml` 重複で WPF ビルドが落ちる問題を防ぐため。
- `ThumbnailProgressRuntime` の完了パネル再利用時プレビュー保持を復元した。
  - 次ジョブ開始時に旧 `PreviewImagePath` を消さず、次サムネ到着まで前回画像を見せ続ける。
  - `LastAppliedPreviewJobKey` だけクリアし、保存済み画像の再上書き抑止は維持する。
- 確認結果:
  - `Debug|x64` ソリューションビルド成功
  - Queue / FailureDb / snapshot / failed tab / progress runtime 系の対象テスト 61 件成功

## 16. 2026-03-11 Running停滞 watchdog メモ
- `ExecuteWithLeaseHeartbeatAsync(...)` にジョブ単位 watchdog timeout を追加する。
- 返ってこない `Running` は `TimeoutException` として既存の `HangSuspected` 経路へ流す。
- lane 別の初期 timeout は固定値で開始する。
  - normal: 3 分
  - recovery: 5 分
  - slow: 10 分
- まずは難動画の「入ったまま返ってこない」を止めることを優先し、動的閾値化は後段とする。

## 17. 2026-03-11 lease未着手残留 回収メモ
- `StartedAtUtc=''` のまま残る `Processing` は、worker が生存していても coordinator の需要判定を揺らしうる。
- `ResetStaleProcessingToPending(...)` に 20 秒 grace を追加し、未着手 lease が一定時間を超えたら `Pending` へ戻す。
- これにより、recovery 需要の誤検出で `fast=13 / slow=1` と `fast=1 / slow=13` を往復する worker 再起動ループを抑制する。
- `QueueDbDemandSnapshotTests.GetDemandSnapshot_StartedBlankLeaseOlderThanGrace_IsReturnedToQueued` を追加し、owner 生存中でも回収されることを固定した。

## 18. 2026-03-11 idle/recovery 無言停止 観測ログメモ
- `ラ・ラ・ランド 2_2` は normal 側では `retry-scheduled` まで進むが、idle/recovery 側で `engine selected: id=autogen` の後に無言停止する。
- 停止位置を 1 段狭めるため、`ThumbnailQueueProcessor` に以下の観測ログを追加した。
  - `consumer dispatch begin`
  - `consumer lane entered`
  - `consumer running marked`
  - `consumer processing invoke`
  - `consumer processing watchdog start`
  - `consumer processing action begin/returned/completed/canceled/faulted`
- 目的は `ProcessLeasedItemAsync` 入口から `processingAction` 返却までのどこで止まるかを実機ログだけで切り分けること。
- 原因が確定したら、この観測ログは整理または削減する。

## 19. 2026-03-11 normal 先逃がしメモ
- 通常レーンだけ短い時間予算を掛け、難動画が normal job を長く塞がないようにした。
- 初期値は `10秒` で、`IMM_THUMB_NORMAL_LANE_TIMEOUT_SEC` で実機上書きできる。
  - 例: `15` を指定すると normal lane のみ 15 秒で救済判定する。
  - `manual` と `IsRescueRequest=1` は対象外とする。
- timeout 時は `thumbnail-timeout` ログを出し、同じ Queue 行を `ForceRetryMovieToPending(..., promoteToRecovery: true)` で救済へ戻す。
- engine が失敗を返した場合も、通常レーン起点なら `thumbnail-recovery` ログを出して救済へ昇格する。
- ffmpeg1pass の cancel 時は外部プロセスを `Kill(entireProcessTree: true)` し、2 秒待って残留を抑える。
- 実機確認では以下を揃えて読む。
  - `thumbnail-timeout`
  - `thumbnail-recovery`
  - `consumer dispatch begin`
  - `consumer lane entered`
  - `consumer processing watchdog start`

## 20. 2026-03-12 ラ・ラ・ランド 2_2 recovery 実機確認メモ
- `repair prepare begin` で止まる問題は外れ、recovery worker は次まで進むことを実機で確認した。
  - `index-probe probe result`
  - `repair prepare end`
  - `execution flow begin`
  - `engine selected: id=ffmpeg1pass`
- `ffmpeg1pass` の cancel/timeout 問題とは別に、`ffmpeg` 自体は `exit success` なのに出力 JPEG が無い経路が存在した。
  - 追加ログ `ffmpeg1pass-output` で `ok=True` / `exists=False` / `size=0` を観測済み。
  - 失敗文言は `ffmpeg exited successfully but produced no output file` として切り分けた。
- `1x1` タイル時は `-ss` で取得地点を決めているため、`fps/tile` を外して単発抽出寄りの filter に変更した。
- それでも `ffmpeg1pass` 単独では外れる個体があり、`workthree` 実績どおり終端 `opencv` 救済が必要と判断した。

## 21. 2026-03-12 workthree 合流メモ
- recovery で `selected=ffmpeg1pass` の時、現行 policy はそこで打ち止めになり `opencv` へ落ちていなかった。
- `ThumbnailExecutionPolicy.BuildEngineOrderIds(...)` を修正し、`ffmpeg1pass -> opencv` の終端 fallback を復元した。
- 実機ログで下記の並びを確認済み。
  - `ffmpeg1pass-output ... ok=True ... exists=False`
  - `engine failed: category=error id=ffmpeg1pass ... try_next=True`
  - `engine fallback: category=fallback from=ffmpeg1pass, to=opencv, attempt=2/2`
  - `execution flow end ... success=True`
  - `repair success`
- これにより `ラ・ラ・ランド 2_2.mp4` は、`future` でも `workthree` と同じく「one-pass が外れたら最後は opencv で救う」流れへ戻せた。
- 未解決は 1 点。
  - `repair success` 後に同じ `QueueId=1710` をもう一度 lease 取得する区間があり、成功後の再取得抑止は別途確認が必要。

## 22. 2026-03-12 success直後の再取得 原因メモ
- `repair success` 後に同じ `QueueId=1710` が再取得された件は、`Done` 更新の取りこぼしではなかった。
- 根拠:
  - `ThumbnailQueue` 上では一度 `Status=Done` まで更新されていた。
  - その直後に `debug-runtime.log` で watch 側の通常 `enqueue accepted` と `persister upsert` が走っていた。
  - さらに `21:36:00` 台で同じ行が再度 `lease acquired` されていた。
- つまり実際に起きていたのは「成功後の watch 再投入で、同条件の `Done` 行が `Pending` に戻される」だった。
- 本線の最小修正方針:
  - `QueueDbService.Upsert(...)` で `Done` 行は無条件に戻さない。
  - ただし `MovieSizeBytes / ThumbPanelPos / ThumbTimePos` のどれかが変わった時だけは、正当な再作成として `Pending` に戻す。
- これにより、watch の定常再投入ノイズだけ止めつつ、設定変更やファイル差し替えは従来どおり再生成できる。

## 23. 2026-03-12 みずがめ座 `CODEC NG` 誤判定と `Failed` 再取得ループ メモ
- 対象:
  - `E:\_サムネイル作成困難動画\開発用\みずがめ座\みずがめ座 (2).mp4`
- `workthree` では `repair workflow` 後に `autogen` / `ffmpeg1pass` 成功実績がある個体だが、`future` では一度 `placeholder-unsupported` で `CODEC NG` 化していた。
- 実機ログを追うと、recovery 中の decode error は `UnsupportedCodec` より「途中破損」寄りだった。
  - `Invalid NAL unit size`
  - `missing picture in access unit`
  - `Error splitting the input into NAL units`
  - `Error submitting packet to decoder`
  - `Decoding error`
  - `Decode error rate`
- 対応:
  - `ThumbnailPlaceholderUtility` で上記キーワード群を `CorruptionButNotUnsupportedKeywords` として切り出し、`FailurePlaceholderKind.None` を返すようにした。
  - `ThumbnailEngineExecutionCoordinator` では `FailurePlaceholderKind.None` の時に placeholder を作らず、
    - `PolicyDecision=placeholder-suppressed`
    - `PlaceholderAction=skipped`
    を result へ残すようにした。
- 実機確認:
  - normal lane `15秒 timeout` で rescue へ退避
  - recovery 側で `ffmpeg1pass -> opencv` まで試行
  - 終端は
    - `failure placeholder suppressed`
    - `thumbnail create failed`
    - `repair failed`
  - となり、新しい `CODEC NG` は生成されない
- 追加で見えた別問題:
  - `repair failed` の直後に watcher の通常 `Upsert` が同じ `Failed` 行を `Pending` に巻き戻し、同一 `QueueId` をすぐ再 dispatch していた
- 対応:
  - `QueueDbService.Upsert(...)` で、`Failed` 行も `Done` 行と同様に「同条件の通常再投入では戻さない」ようにした
  - ただし `MovieSizeBytes / ThumbPanelPos / ThumbTimePos` に差分がある場合だけは、正当な再作成として `Pending` に戻す
- 実機再確認:
  - `Failed` 行を置いた状態で本体起動
  - watcher の `enqueue accepted` は発生
  - それでも `persister upsert` は `db_affected=0`
  - `consumer dispatch begin` は新規発生せず
  - QueueDB も `Status=Failed` を維持
- これで `みずがめ座` 系は
  - `CODEC NG` 誤成功化を止める
  - `repair failed` 後の即時再取得ループを止める
  まで本線で固定できた。
