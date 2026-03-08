# Implementation Plan: サムネイルWorker完全責務移譲 長期計画（2026-03-08）

## 1. 目的
- サムネイル関連の実行責務を、最終的に UI 本体から Worker 側へ寄せ切る。
- UI は「設定編集・状態確認・手動操作」に集中し、重い判断と実行は Worker が担う構成へ移行する。
- Windows プライオリティ、並列数、レーン判定、進捗、ログ、再試行方針を Worker 起点で一貫管理できる状態を作る。

## 2. この計画の結論
- 目指すべき最終形は「MainWindow は Control Plane、Worker は Data Plane」である。
- 具体的には、UI 本体は MainDB を開いて設定を保存し、Worker 群の起動監視と viewer 起動だけを行う。
- サムネイル処理の実効設定値、レーン判定、処理実行、進捗スナップショット、詳細ログは Worker 側で決定・保持する。
- 進捗表示も最終的には本体内の重いUIから切り離し、外部 viewer を正規経路とする。

## 3. 背景
- 現在は Worker 化と外部 progress snapshot 化までは入っているが、設定値の最終解釈はまだ UI 側に多く残っている。
- そのため、以下の曖昧さが残っている。
  - 並列数やプリセットの「正」が UI と Worker のどちらにあるか曖昧
  - MainWindow が Worker 起動引数を細かく決めており、Worker が薄い executor に留まっている
  - 進捗表示は別窓化したが、状態の意味付けが UI と Worker で二重化しやすい
- この状態のままだと、将来の機能追加で UI 側ロジックが再肥大化し、Worker 分離の価値が薄れる。

## 4. この計画で扱う範囲
- サムネイル設定責務の Worker 側移譲
- Worker 実行ポリシーの一本化
- progress viewer の正規化
- Worker 向け設定スナップショットと状態スナップショットの永続化
- ログと障害解析経路の整理

## 5. この計画で扱わない範囲
- WhiteBrowser 互換 DB 本体のスキーマ変更
- 動画インデックス修復機能そのものの大幅再設計
- Watcher 全体の責務移譲
- サムネイル以外の UI 分離

## 6. 前提条件
- WhiteBrowser の DB（`*.wb`）は変更しない。
- QueueDB と Worker 補助ファイルはアプリ独自管理でよい。
- x64 / Visual Studio 2026 / MSBuild 運用を前提にする。
- Worker が存在しない環境では、当面はフォールバックを残してよい。

### 6.1 並行開発中の実装方針
- 現在は別作業が走っているため、既存本線ファイルへ直接大きく混ぜないことを優先する。
- 新しい責務移譲やポリシー移譲は、まず別ファイルで独立実装すること。
- 既存ファイルへの変更は、呼び出し口の追加や切替点の最小追加に留めること。
- 先行実装した別ファイルは、後で本線へ統合できる粒度で分離しておくこと。
- 既存経路を置換するのは、別作業の収束後に統合フェーズとして行うこと。

この方針で優先する形:
- `*Policy.cs`
- `*Resolver.cs`
- `*Service.cs`
- `*Snapshot.cs`
- `*ProcessManager.cs`
- `*IntegrationPlan*.md`

避けること:
- `MainWindow.xaml.cs`
- `ThumbnailCreationService.cs`
- `ThumbnailQueueProcessor.cs`
- 既存UI本線ファイル
への広範囲な直接埋め込み

## 7. 最終到達イメージ

### 7.1 役割分担
- `IndigoMovieManager_fork.exe`
  - MainDB オープン
  - 設定編集
  - Queue 入力
  - Worker supervisor
  - progress viewer supervisor
- `IndigoMovieManager.Thumbnail.Worker.exe --role normal`
  - 通常系ジョブ処理
  - 実効設定解釈
  - `BelowNormal`
- `IndigoMovieManager.Thumbnail.Worker.exe --role idle`
  - 巨大動画 / 再試行 / `ffmpeg.exe`
  - 実効設定解釈
  - `Idle`
- `IndigoMovieManager.Thumbnail.ProgressViewer.exe`
  - 外部スナップショット読取
  - 詳細進捗表示

### 7.2 設定の正
- UI が保存するのは「ユーザー設定値」
- Worker が決めるのは「実効設定値」
- viewer は Worker が出す状態だけを表示する

## 8. 要件

### 8.1 機能要件

#### FR-01 設定スナップショット
- サムネイル設定は Worker が読める単一スナップショットへまとめること。
- 少なくとも以下を含むこと。
  - プリセット
  - 並列数
  - 巨大動画判定GB閾値
  - GPUデコード使用可否
  - サムネイル縮小可否
  - レーン優先方針
  - retry 方針
  - poll interval
  - batch cooldown

#### FR-02 Worker 側設定解釈
- UI は実効並列数や role 別並列数を直接決めないこと。
- Worker がスナップショットから role ごとの実効値を解決すること。

#### FR-03 role 固定実行
- `normal` と `idle` の意味を Worker 側で固定管理すること。
- `ffmpeg.exe` は `idle` role 配下だけで起動すること。

#### FR-04 progress 外部化
- ジョブ開始、保存、完了、失敗、待機状態は Worker が外部スナップショットへ反映すること。
- UI 本体はメモリ状態を正とせず、外部スナップショットを読むこと。

#### FR-05 viewer 正規化
- 詳細進捗表示は `ThumbnailProgressViewer.exe` を正規表示経路とすること。
- 本体下部タブは要約表示に留めること。

#### FR-06 ログ整理
- Worker と UI のログ責務を分離すること。
- 実行判断ログは Worker、画面操作ログは UI に残すこと。

#### FR-07 DB切替安全性
- MainDB 切替時、旧 DB 用 Worker と viewer は停止し、新 DB 用へ差し替えること。
- 異なる DB を誤って処理しないこと。

#### FR-08 障害時縮退
- Worker 起動失敗時は、理由がログに残ること。
- 必要に応じて in-process fallback の有無を段階的に制御できること。

### 8.2 非機能要件

#### NFR-01 UI 応答性
- UI 本体の優先度は常に `Normal` を維持すること。
- サムネイル処理の設定変更や詳細描画が、一覧操作を阻害しないこと。

#### NFR-02 責務の一意性
- 「設定の解釈」と「進捗の意味付け」は Worker 側に一意化すること。
- UI は同じ判断ロジックを再実装しないこと。

#### NFR-03 観測性
- ログと progress snapshot だけで、どの DB を誰がどう処理しているか追えること。

#### NFR-04 互換性
- 既存 MainDB と QueueDB の基本運用を壊さないこと。
- Worker 未配置時の既存利用者への影響を段階導入で抑えること。

#### NFR-05 拡張性
- 将来 role を増やす場合でも、UI と Worker の結合を増やさないこと。

## 9. 設計原則
- UI は「命令」と「表示」に徹し、判断を持ち過ぎない。
- Worker は「判断」と「実行」に責任を持つ。
- 進捗は UI メモリではなく外部状態を正とする。
- 起動引数で値を細かく渡し過ぎず、スナップショット参照へ寄せる。
- role 名と Windows プライオリティの関係は固定する。

## 10. アーキテクチャ方針

### 10.1 Control Plane
- `MainWindow`
- 設定編集UI
- Worker supervisor
- viewer supervisor
- Queue input

### 10.2 Data Plane
- `WorkerCore`
- `Worker.exe`
- Engine / Queue 実処理
- progress snapshot publisher
- 実行ログ

### 10.3 状態ファイル
- `worker-settings.json`
  - DB単位またはセッション単位で保存
- `thumbnail-progress-*.json`
  - owner 単位の状態
- `thumbnail-health-*.json`
  - owner 単位の生死と優先度状態
- 将来候補:
  - `worker-stats.json`

## 11. 長期ロードマップ

## 11.0 2026-03-08 時点の実装反映状況
- Phase 1 は実装済み。
- UI は Worker 起動時に個別設定値をばらまかず、設定 snapshot を保存して path と version だけを渡す構成へ移行済み。
- Worker は snapshot を読み、role ごとの実効並列数、priority、poll、cooldown、lease batch、GPU decode、巨大動画閾値を自前で決定する。
- 現時点で UI 側に残る設定ロジックは、fallback 用の in-process consumer と表示用の要約だけである。

実装済み主要ファイル:
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailWorkerSettingsSnapshot.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailWorkerSettingsStore.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailWorkerSettingsResolver.cs`
- `src/IndigoMovieManager.Thumbnail.WorkerCore/ThumbnailWorkerHostService.cs`
- `src/IndigoMovieManager.Thumbnail.WorkerCore/ThumbnailWorkerRuntimeOptions.cs`
- `src/IndigoMovieManager.Thumbnail.Worker/Program.cs`
- `Thumbnail/Worker/ThumbnailWorkerProcessManager.cs`
- `Thumbnail/MainWindow.ThumbnailCreation.cs`

検証結果:
- MSBuild x64 Debug: 成功
- 追加テスト: `ThumbnailWorkerSettingsStoreTests` / `ThumbnailWorkerSettingsResolverTests` 成功
- 2026-03-08 時点で確認できている既知警告は `NETSDK1206` のみ

残課題:
- Phase 2 の lane 判定、retry 方針、engine 切替ポリシーはまだ UI と WorkerCore に分散している。
- fallback 経路の設定解釈は将来縮退対象であり、現時点では完全撤去していない。
- ただし fallback 側の並列数、poll、cooldown、GPU decode、巨大動画閾値は、`ThumbnailWorkerSettingsResolver` と `ThumbnailWorkerExecutionEnvironment` を通して Worker と同じ解釈へ寄せ始めている。

### Phase 0 現状固定
- 現状の Worker / viewer / snapshot 構成を壊さず、基準線を固定する。
- 既存設定項目と Worker 起動引数の棚卸しを行う。

完了条件:
- 現在どの値を UI が解決し、どの値を Worker が受け取っているか一覧化されている。

### Phase 1 設定スナップショット導入
- `ThumbnailWorkerSettingsSnapshot` を新設する。
- UI は起動引数へ個別値をばらまかず、snapshot path と DB 情報だけを渡す。
- Worker は snapshot を読み、role 別の実効値を決める。

対象:
- 並列数
- プリセット
- 巨大動画閾値
- GPU decode
- resize
- poll interval
- cooldown

完了条件:
- `GetThumbnailQueueMaxParallelism()` 相当の最終判断が UI 側から消えている。

2026-03-08 実装メモ:
- 完了。
- UI は `SaveThumbnailWorkerSettingsSnapshot()` で snapshot を保存し、Worker へは `--main-db` `--owner` `--settings-snapshot` `--role` `--parent-pid` だけを渡す。
- Worker は `ThumbnailWorkerSettingsResolver` で role ごとの `MaxParallelism` `LeaseBatchSize` `ProcessPriority` `FfmpegPriority` `PollIntervalMs` `BatchCooldownMs` を決める。
- `GpuDecodeEnabled` と `SlowLaneMinGb` も Worker 側で environment へ反映する。

### Phase 2 Worker 実行ポリシー一本化
- lane 判定
- retry 方針
- `autogen` / `ffmpeg1pass` 切替
- role ごとの取得条件
- role ごとの priority
を WorkerCore 側へ集約する。

完了条件:
- UI 側から queue 実行ポリシーの知識がほぼ消える。

2026-03-08 着手メモ:
- 一部着手済み。
- `MainWindow.xaml.cs` の `GetThumbnailQueueMaxParallelism()` などは、直接 `ThumbnailThreadPresetResolver` を叩かず `ThumbnailWorkerSettingsResolver` 経由へ変更した。
- in-process fallback も `ThumbnailWorkerExecutionEnvironment` を通して `IMM_THUMB_GPU_DECODE` `IMM_THUMB_SLOW_LANE_MIN_GB` `IMM_THUMB_PROCESS_PRIORITY` `IMM_THUMB_FFMPEG_PRIORITY` を Worker と同じ値へ寄せる。
- `ThumbnailCreationService` に残っていた `autogen retry`、`ffmpeg1pass skip`、`forced repair`、`recovery onepass fallback` の判定は `ThumbnailExecutionPolicy` へ切り出し済み。
- `BuildThumbnailEngineOrder()` も engine 実体ではなく engine id ベースの順序解決へ変更し、`ThumbnailExecutionPolicy.BuildEngineOrderIds()` を正規経路にした。
- `ThumbnailEngineCatalog` を新設し、engine id と engine 実体の対応、重複除去つき順序生成を service 外へ移した。
- `ThumbnailEngineExecutionCoordinator` を新設し、`ExecuteEngineOrderAsync` 相当の engine 実行ループ、autogen retry、near-black 再失敗化、known invalid input による onepass skip を service 外へ移した。
- 同 coordinator に post-process を追加し、`recovery onepass fallback`、`repair 後 original onepass 再挑戦`、`failure placeholder` 適用も service 外へ移した。
- さらに `index repair target`、`autogen near-black 判定`、`forced repair 実行条件`、`repair 後 onepass 再挑戦条件`、`failure placeholder skip 条件` も `ThumbnailExecutionPolicy` 側へ寄せた。
- `ThumbnailJobContextFactory` を新設し、通常文脈・original movie 文脈・repair 後文脈の生成を一箇所へ寄せた。
- `ThumbnailJobMaterialBuilder` を新設し、duration / thumbInfo / bitrate / codec の素材収集を service 外へ寄せた。
- `ThumbnailFailureFinalizer` を新設し、error marker 出力を service 外へ移した。
- `ThumbnailProcessLogFinalizer` を新設し、`thumbnail-create-process.csv` の最終記録を service 外へ寄せた。
- `ThumbnailPreflightChecker` を新設し、`manual target missing`、`missing movie`、`DRM precheck`、`unsupported precheck` を service 外へ寄せた。
- `ThumbnailResultFinalizer` を新設し、error marker、duration cache 更新、process log をまとめて service 外へ寄せた。
- `ThumbnailRepairRerunCoordinator` を新設し、forced repair 成功後の文脈再構成と engine 再実行を service 外へ寄せた。
- `ThumbnailRepairWorkflowCoordinator` を新設し、事前 `index probe/repair` と失敗後 `forced repair` の実行、repair ログ、repair 後 codec 解決を service 外へ移した。
- `ThumbnailRepairExecutionCoordinator` を新設し、repair 準備から forced repair 後 rerun までの state 管理を service 外へ寄せた。
- SWF 専用分岐は `Thumbnail/Swf/SwfThumbnailRouteHandler.cs` へ移し、`ThumbnailCreationService` には route handler 呼び出しだけを残した。
- `ThumbnailResultCompletionCoordinator` を新設し、`CreateThumbAsync` 内の local function だった return 締め処理を service 外へ寄せた。
- `ThumbnailExecutionFlowCoordinator` を新設し、context 組み立て、engine 実行、repair apply、post process の主フローを service 外へ寄せた。
- `ThumbnailCreationOrchestrationCoordinator` を新設し、preflight、SWF 分岐、material build、主フロー呼び出し、completion 接続を service 外へ寄せた。
- `ThumbnailResultFactory` を新設し、`ThumbnailCreateResult` の success / failed 生成を service 外へ寄せた。
- `ThumbnailImageUtility` を新設し、preview frame 生成、frame retry、auto/swf `ThumbInfo`、crop/resize、combined JPEG 保存を service 外へ寄せた。
- `ThumbnailShellMetadataUtility` を新設し、Shell 経由 duration 取得と COM 解放を service 外へ寄せた。
- `ThumbnailPlaceholderUtility` を新設し、failure kind 判定、placeholder 生成、固定エラー画像複製を service 外へ寄せた。
- `ThumbnailMovieMetaCache` を新設し、hash 決定、duration cache、DRM/SWF 事前判定、cache key 管理を service 外へ寄せた。
- `ThumbnailOutputLockManager` を新設し、出力ファイル単位の排他辞書と寿命管理を service 外へ寄せた。
- `ThumbnailCsvUtility` を新設し、`thumbnail-create-process.csv` のCSV整形と書き込みを service 外へ寄せた。
- `FrameDecoderThumbnailGenerationEngine`、`FfmpegAutoGenThumbnailGenerationEngine`、`FfmpegOnePassThumbnailGenerationEngine`、`SwfThumbnailRouteHandler`、`ThumbnailPreflightChecker`、`ThumbnailEngineExecutionCoordinator`、`ThumbnailCreationOrchestrationCoordinator`、`ThumbnailJobMaterialBuilder` は、`ThumbnailCreationService` wrapper 経由ではなく上記 helper を直接参照する形へ更新した。
- `ThumbnailPreflightChecker`、`ThumbnailEngineExecutionCoordinator`、`SwfThumbnailRouteHandler` は placeholder 系も `ThumbnailCreationService` 経由ではなく `ThumbnailPlaceholderUtility` を直接参照する形へ更新した。
- `ThumbnailCreationService` から JPEG 保存 retry 用の死んだ private helper 群を削除し、wrapper は回帰テスト互換の薄い橋だけを残す構成へ整理した。
- `ThumbnailCreationService` に残していた placeholder 実装本体も helper 委譲へ切り替え、service 内には互換 wrapper だけを残した。
- `ThumbnailCreationService` に残していた `CachedMovieMeta` 系の実装本体も helper 委譲へ切り替え、service では lookup と completion 接続だけを行う形へ整理した。
- `ThumbnailCreationService` に残していた output lock 実装本体も helper 委譲へ切り替え、service では acquire / release の橋だけを行う形へ整理した。
- テスト側も `ThumbnailResultFactory` と `ThumbnailImageUtility` を直接参照する形へ寄せ、`CreateSuccessResult` `CreateFailedResult` `CreatePreviewFrameFromBitmap` などの service wrapper を削除した。
- `ThumbnailProcessLogFinalizer` は `ThumbnailCsvUtility` を直接使う形へ更新し、service から process log のCSV整形本体を外した。
- `AutogenRegressionTests` は `ThumbnailCreationService.BuildThumbnailEngineOrder()` への reflection 依存をやめ、`ThumbnailEngineCatalog` を直接使う形へ変更した。
- `ThumbnailProcessLogFinalizer` は自前の lock と log file name を持つ形へ更新し、service から `process log sync root` の橋も外した。
- `ThumbnailCreationService` に残っていた placeholder 互換 wrapper と output lock の橋も削除し、service から helper 直委譲のためだけの薄い static / private bridge を外した。
- `ThumbnailMemoryLeakPreventionTests` も `ThumbnailOutputLockManager` を直接見る形へ寄せた。
- `ThumbnailCreationFacade` を新設し、`CreateThumbAsync` に残っていた request 組み立て、cache lookup、save path 解決、output lock、completion coordinator 構築、cleanup を service 外へ寄せた。
- `ThumbnailCreationRuntime` と `ThumbnailCreationRuntimeFactory` を新設し、service と WorkerCore が同じ runtime / facade 配線を共有する形へ寄せた。
- `ThumbnailWorkerHostService` は `ThumbnailCreationService` 経由ではなく `ThumbnailCreationRuntime.CreateThumbAsync(...)` を呼ぶ形へ変更し、Worker 側の入口も facade と同じ規約に揃えた。
- `CreateBookmarkThumbAsync`、`ProbeVideoIndexAsync`、`RepairVideoIndexAsync` も `ThumbnailCreationRuntime` へ寄せ、service は runtime への公開委譲だけを持つ形に整理した。
- `ThumbnailCreationRuntimeProvider` を新設し、service constructor 群から runtime 生成責務を外した。`ThumbnailCreationService` は shell として provider から runtime を受け取り、公開 API を委譲する形に整理した。
- `ThumbnailCreationServiceFactory` を新設し、テストや内部配線で必要だった engine 差し替え用 constructor 群を service 本体から外した。service 側には公開 constructor と shell 用の最小内部入口だけを残した。
- `ThumbnailFallbackModeResolver` を新設し、通常運用は external worker 必須、in-process fallback は `IMM_THUMB_ALLOW_INPROCESS_FALLBACK` 明示許可時または debugger attach 時だけに使う形へ縮退を開始した。
- `ThumbnailWorkerSettingsSnapshot` に `AllowFallbackInProcess` を追加し、UI が保存する生設定スナップショットにも fallback 縮退状態を含めるようにした。
- `CheckThumbAsync` は worker 未配置時、fallback が無効なら即 in-process consumer へ落ちず、worker 配置を待って external supervisor 本線へ戻る形に変更した。
- `ThumbnailWorkerHealthSnapshot` `ThumbnailWorkerHealthStore` `ThumbnailWorkerHealthPublisher` を追加し、health snapshot 契約を別ファイルで導入した。
- `ThumbnailWorkerHostService` は `starting / running / stopped / exited` を owner 単位の health snapshot へ publish するようにし、2秒 heartbeat で現在優先度を更新する形へ変更した。
- `ThumbnailWorkerProcessManager` は `missing / start-failed / exited / stopped` を publish するようにし、worker 未配置と worker 即死を Viewer 側で見分けられるようにした。
- `ThumbnailProgressViewerWindow` は progress に加えて health snapshot を読むようにし、ヘッダに `通常 / ゆっくり` の状態を要約表示する形へ変更した。
- health snapshot には `ReasonCode` を追加し、`worker-missing / process-start-failed / db-mismatch / dll-missing / exception / graceful-stop / canceled` を区別できるようにした。
- Viewer と MainWindow 下部要約は `ReasonCode` を人間向け文言へ変換して表示するようにし、`DLL不足` や `DB不一致` をログを開かずに追える形へ寄せた。
- 現時点で `ThumbnailCreationService` に残るのは、各 coordinator の接続と公開 API の窓口が中心である。

2026-03-08 検証メモ:
- MSBuild x64 Debug: 成功
- 関連回帰テスト: 28/28 pass
- 対象:
  - `AutogenExecutionFlowTests`
  - `AutogenRegressionTests`
  - `ThumbnailWorkerSettingsStoreTests`
  - `ThumbnailWorkerSettingsResolverTests`
  - `ThumbnailWorkerExecutionEnvironmentTests`
- 追加検証:
  - `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj` x64 Debug build: 成功
  - `Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj` x64 Debug build: 成功
  - `SwfThumbnailSupportTests` + `AutogenExecutionFlowTests`: 21/21 pass
- 補足:
  - `ThumbnailEngineCatalog.cs` は engine 専用共有ファイルのため、本体WPF側 compile から除外して project 境界を維持した。
  - `ThumbnailEngineExecutionCoordinator.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailPreflightChecker.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailJobMaterialBuilder.cs` と `ThumbnailProcessLogFinalizer.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailRepairRerunCoordinator.cs` と `ThumbnailResultFinalizer.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailRepairWorkflowCoordinator.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailResultCompletionCoordinator.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailRepairExecutionCoordinator.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailExecutionFlowCoordinator.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailCreationOrchestrationCoordinator.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailResultFactory.cs`、`ThumbnailImageUtility.cs`、`ThumbnailShellMetadataUtility.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailPlaceholderUtility.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailMovieMetaCache.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailOutputLockManager.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailCsvUtility.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailCreationFacade.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailCreationRuntime.cs` と `ThumbnailCreationRuntimeFactory.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailCreationRuntimeProvider.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `ThumbnailCreationServiceFactory.cs` も同様に engine 専用共有ファイルとして本体WPF側 compile から除外した。
  - `Thumbnail/Swf/SwfThumbnailRouteHandler.cs` は SWF 側責務維持のため `Thumbnail/Swf` 配下へ置き、本線 service では route handler だけを呼ぶ形にした。

Phase 2 の残課題:
- precheck の一部を、将来 `WorkerCore` 側の executor input builder / preflight checker へさらに寄せる
- SWF 別経路も含めた preflight 契約の統一と、`CreateThumbAsync` の early return さらなる削減
- `ThumbnailJobContext` を受け取る executor 入力をさらに整理し、coordinator 間 DTO を減らす
- fallback 自体を縮退機能へ落とし、本線を external worker に一本化する
- policy 変更時の回帰確認を service 経由テストだけでなく、snapshot / role 単位の統合テストへ広げる

### Phase 3 progress 契約の明確化
- progress snapshot のスキーマを明文化する。
- `started / saved / completed / failed / waiting / owner / db / role / movie` を固定化する。
- viewer はその契約だけを読む。

完了条件:
- UI と viewer が Worker 内部状態に直接依存しない。

2026-03-08 実装メモ:
- progress 自体は `thumbnail-progress-*.json` の外部 snapshot を正として読む形へ移行済み。
- 今回 health を `thumbnail-health-*.json` と分離したため、progress 契約は「作業状況」、health 契約は「生死と起動状態」と責務分離できた。
- `ThumbnailProgressRuntimeSnapshot` に `SchemaVersion` を追加し、worker item に `WorkerRole` `State` `MainDbFullPath` `OwnerInstanceId` `MovieFullPath` `UpdatedAtUtc` を持たせる形へ着手した。
- Worker 側では `started / saved / completed / failed` を runtime から明示 publish し、publisher 側で `db / owner` を正規化して envelope へ保存する。
- `WaitingWorkers` を progress 契約へ追加し、固定スロット上の待機状態も snapshot 自体で明示する形へ進めた。
- MainWindow 下部要約は `viewer状態 / progress集計 / worker health` の3段構成に整理し、異常時だけ色と太字で強調する方針へ進めた。
- 残りは field 一覧のドキュメント固定と、Viewer / UI 側で `waiting` の意味文言を揃えることである。

### Phase 4 viewer 正規運用化
- 別窓 viewer を正式 UI とする。
- 本体下部タブは要約・再起動・状態確認だけにする。
- 必要なら viewer 側にフィルタ、role 切替、エラー一覧を追加する。

完了条件:
- 本体で重い worker panel を持たない。

### Phase 5 Worker ヘルス管理
- 起動失敗
- 即死
- 設定不整合
- 依存 DLL 欠落
- DB パス不正
を health 状態として見える化する。

完了条件:
- 「作成が始まらない」時に、ログだけでなく viewer や UI 要約でも原因が読める。

2026-03-08 実装メモ:
- `ThumbnailWorkerHealthSnapshot` を導入し、owner ごとに `thumbnail-health-*.json` を保存する形へ着手済み。
- Worker 本体は `starting / running / stopped / exited` を publish し、Supervisor 側は `missing / start-failed / exited / stopped` を publish する。
- Viewer は `Normal / Idle` owner の health を読み、ヘッダで稼働状態を要約表示する。
- `ReasonCode` により `worker-missing / process-start-failed / db-mismatch / dll-missing` を分類済み。
- MainWindow 下部要約では `failed > 0` や `missing / start-failed / exited` を異常扱いとして強調表示する方針へ進めた。
- 残りは起動失敗理由の種類を増やすことと、UI 強調条件の最終固定である。

### Phase 6 フォールバック縮退整理
- in-process fallback を設定または開発モード限定に縮退する。
- 通常運用は external worker 必須へ寄せる。

完了条件:
- 実運用の主経路が一本化される。

### Phase 7 Worker 設定ホットリロード
- 設定変更を Worker 再起動なしで反映するか、軽量再起動で反映する。
- snapshot version を導入し、viewer も追従できるようにする。

完了条件:
- 並列数や GPU 設定変更が安全に反映される。

### Phase 8 監視系との境界整理
- Watcher からサムネイル救済や再投入を行う際の契約も Worker ルールへ合わせる。
- 欠損救済や投入上限も、Worker 実効設定と噛み合う形へ揃える。

完了条件:
- Watcher と Thumbnail の設定意味が食い違わない。

## 12. 実装単位

### 12.1 新規または拡張対象
- `src/IndigoMovieManager.Thumbnail.WorkerCore`
- `src/IndigoMovieManager.Thumbnail.Worker`
- `src/IndigoMovieManager.Thumbnail.ProgressViewer`
- `src/IndigoMovieManager.Thumbnail.Queue`
- `Thumbnail/Worker/ThumbnailWorkerProcessManager.cs`
- `Thumbnail/Worker/ThumbnailProgressViewerProcessManager.cs`
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `MainWindow.xaml.cs`
- `MainWindow.xaml`

### 12.2 新規コンポーネント候補
- `ThumbnailWorkerSettingsSnapshot`
- `ThumbnailWorkerSettingsStore`
- `ThumbnailWorkerPolicyResolver`
- `ThumbnailWorkerHealthSnapshot`
- `ThumbnailWorkerHealthStore`
- `ThumbnailViewerContract`

## 13. データ契約案

### 13.1 WorkerSettingsSnapshot
- `MainDbFullPath`
- `DbName`
- `ThumbFolder`
- `Preset`
- `RequestedParallelism`
- `SlowLaneMinGb`
- `GpuDecodeEnabled`
- `ResizeThumb`
- `AllowFallbackInProcess`
- `PollIntervalMs`
- `BatchCooldownMs`
- `Version`
- `UpdatedAt`

### 13.2 WorkerHealthSnapshot
- `SchemaVersion`
- `WorkerRole`
- `OwnerInstanceId`
- `MainDbFullPath`
- `State`
- `ReasonCode`
- `SettingsVersionToken`
- `CurrentPriority`
- `Message`
- `ProcessId`
- `ExitCode`
- `UpdatedAtUtc`
- `LastHeartbeatUtc`

### 13.3 ProgressRuntimeSnapshot
- `SchemaVersion`
- `Version`
- `SessionCompletedCount`
- `SessionTotalCount`
- `CurrentParallelism`
- `ConfiguredParallelism`
- `EnqueueLogs`
- `ActiveWorkers`
- `WaitingWorkers`

### 13.4 ProgressWorkerSnapshot
- `WorkerId`
- `WorkerLabel`
- `WorkerRole`
- `State`
- `MainDbFullPath`
- `OwnerInstanceId`
- `DisplayMovieName`
- `MovieFullPath`
- `PreviewImagePath`
- `PreviewCacheKey`
- `PreviewRevision`
- `IsActive`
- `UpdatedAtUtc`

## 14. 段階導入ルール
- 一気に UI から全ロジックを剥がさない。
- まず snapshot 導入で「設定の正」を移す。
- 次に policy 解決を WorkerCore へ寄せる。
- 最後に fallback と旧 UI 依存を削る。
- 並行開発中は、各 Phase の成果物をまず別ファイルで作り、統合は後段フェーズへ送る。
- 統合前でも意味が通るよう、別ファイル単位で責務と依存方向を固定する。

## 15. 主要リスク
- UI と Worker で snapshot schema がズレるリスク
- DB 切替時に古い Worker が残るリスク
- Worker 健康状態が見えず、停止原因が再び分かりづらくなるリスク
- watcher 側の救済処理が旧前提のまま残り、投入量だけ肥大化するリスク
- viewer が詳細表示を持ち過ぎて再度重くなるリスク

## 16. リスク対策
- schema version を入れる
- parent pid と DB path を必ず持たせる
- health snapshot を別契約で持つ
- Queue 入力上限と progress 状態を連動させる
- viewer は表示専用に徹し、処理判断を持たせない

## 17. 要件に対する受け入れ条件

### AC-01
- UI が Worker role 別並列数を直接計算していないこと

### AC-02
- Worker が snapshot だけで実効設定を決定できること

### AC-03
- progress viewer が本体を落とさず単独再起動できること

### AC-04
- DB切替後に旧DB向け Worker / viewer が残らないこと

### AC-05
- `normal` と `idle` の優先度が role 固定でログに残ること

### AC-06
- サムネイル設定変更時、反映経路が `UI保存 -> snapshot更新 -> Worker適用` と一本化されていること

### AC-07
- 並行開発期間中の新規実装が、既存本線ファイルへの大規模編集なしで成立していること

## 18. いつでも残すべき不変条件
- UI は `Normal`
- `ffmpeg.exe` は `Idle` 系
- WhiteBrowser DB は変更しない
- MainDB 切替時は旧 owner を残さない
- Worker が扱う DB と snapshot は必ず対応付く

## 19. 優先順位
1. 設定スナップショット導入
2. Worker 側設定解釈
3. policy 解決の WorkerCore 集約
4. health snapshot
5. viewer 正規運用化
6. fallback 縮退
7. watcher 境界整理

## 20. 推奨する次の着手
- 並行開発中は、まず別ファイルで責務単位を作る。
- 具体的には `ThumbnailWorkerSettingsSnapshot` `ThumbnailWorkerSettingsStore` `ThumbnailWorkerSettingsResolver` のように、既存本線から独立した単位を先に増やす。
- MainWindow や既存サービス本体の変更は、参照口の追加だけに留める。
- 別作業の収束後に、既存経路を新単位へ順次差し替える統合フェーズへ進む。

## 22. 2026-03-08 実装反映メモ
- 下部 `サムネイル` タブの要約は 1 つの文章から 3 ブロック表示へ変更済み。
  - viewer状態
  - progress状態
  - worker health状態
- 強調表示も全体一括ではなく、行単位の severity 反映へ変更済み。
- 本体 full build は `wpftmp` 重複属性問題を解消済み。
  - `src\**` と `tools\SwfAirThumbnailProbe\**` の本体巻き込みを入口で除外
  - `ForceIncludeGeneratedWpfCode` は `wpftmp` では無効化
  - `g.cs` の二重注入も抑止
- 2026-03-08 時点の本体 `Build` は成功。
  - `MSBuild.exe /t:Build /p:Configuration=Debug /p:Platform=x64 /p:Restore=false`
-  - `0 warning / 0 error`
- 2026-03-08 時点のテスト全体は成功。
  - `dotnet test Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64`
  - `255 pass / 7 skip / 0 fail`

## 21. 関連ドキュメント
- `Thumbnail/Implementation Plan_サムネイルDLL化_Windowsプライオリティ明確化_2026-03-08.md`
- `Thumbnail/ManualCheck_サムネイルWorker実機確認チェックリスト_2026-03-08.md`
- `Thumbnail/仕様書_サムネイル並列方式再設計_2026-03-08.md`
- `Docs/アプリ全体図_大まか構成_2026-03-08.md`
