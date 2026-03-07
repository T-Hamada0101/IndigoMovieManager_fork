# Implementation Plan + tasklist（サムネイルスレッド制御とレーン設計, 2026-03-06）

## 0. 目的
- `idle / slow / normal / ballence / fast / max / custum` のプリセットを導入し、サムネイル作成の負荷感をユーザー設定として明確化する。
- 通常レーン、低速レーン、リカバリーレーンの責務を維持したまま、低負荷モードと速度重視モードを両立する。
- 既存の動的並列制御は維持しつつ、プリセット起点の初期解決とUI表現を追加する。

## 1. 実装方針
- 既存の `ThumbnailParallelController` は残し、プリセットはその「初期値 / 上限 / 低負荷ヒント」を与える役割に留める。
- `1スレッド` の意味は、内部実行枠の厳密1本固定ではなく「低負荷モード」として解釈する。
- レーン予約は「対象ジョブが存在する時だけ有効」を原則とし、空振り予約を避ける。
- 既存ユーザー設定との互換のため、移行時は `custum` を逃がし先に使う。
- エラー増加時または高負荷感知時は、動的並列制御で安全側へ縮退させる。
- エラー率や負荷が落ち着いた時は、段階的に復帰させる。
- 高負荷検知の最終判断はオーケストレータ側で行い、エンジンは局所メトリクス供給に留める。
- Disk温度やUsnMftなど管理者権限が絡む情報は、将来の管理者権限サービスから受け取る前提で設計する。
- `CommonSettingsWindow` は `FileIndexProvider` 3択UI反映済みの共有画面であるため、サムネイル設定追加は既存UIとの整合を前提に行う。
- `Watcher` 側の `AdminUsnMft` 境界と競合しないよう、将来の管理者権限サービスとIPCは共通化を前提とする。

## 2. 変更対象
- `CommonSettingsWindow.xaml`
- `CommonSettingsWindow.xaml.cs`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `Thumbnail/ThumbnailProgressRuntime.cs`
- `Thumbnail/ThumbnailParallelController.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailLaneClassifier.cs`
- `Properties/Settings.settings`
- `Properties/Settings.Designer.cs`
- `App.config`
- `Tests/IndigoMovieManager_fork.Tests/*`
- 将来対象:
  - 管理者権限サービス（新規プロセス/サービス）
  - 本体とのIPCインターフェース
  - `Watcher/FileIndexProviderFactory.cs` 周辺との整合確認
  - `Watcher/MainWindow.Watcher.cs` との通知文言整合

## 3. 詳細設計

### 3.1 新規設定値
- `ThumbnailThreadPreset`
  - 値:
    - `idle`
    - `slow`
    - `normal`
    - `ballence`
    - `fast`
    - `max`
    - `custum`
- `ThumbnailParallelism`
  - `custum` 時の手動並列数として維持する。
- 既存の `ThumbnailPriorityLaneMaxMb` / `ThumbnailSlowLaneMinGb` は継続利用する。

### 3.2 プリセット解決
- `idle`
  - `1スレッド相当`
  - 実行時は最小干渉モードフラグを併用する。
- `slow`
  - `1スレッド相当`
  - 実行時は低負荷モードフラグを併用する。
- `normal`
  - 論理コア `1/3` を基準に解決する。
- `ballence`
  - 論理コア `1/4〜1/3` の安全寄り値へ解決する。
- `fast`
  - 論理コア `1/2` へ解決する。
- `max`
  - 安全上限へ解決する。
- `custum`
  - 既存の `ThumbnailParallelism` をそのまま使う。

### 3.3 低負荷モード
- `slow` と `idle` を低負荷系モードとして扱う。
- 実装候補:
  - ポーリング待機を長めにする。
  - レーン処理間に小さな待機を入れる。
  - 必要なら優先度を下げる。
- 初期実装:
  - `slow`:
    - キューのポーリング間隔を通常の2倍にする。
    - バッチ完了後に `750ms` のクールダウンを入れる。
    - ワーカー優先度は `BelowNormal` 候補。
  - `idle`:
    - 実効並列1。
    - プレビュー停止または遅延。
    - UI更新を `250ms-500ms` 以上で間引く。
    - 保存並列1。
    - ワーカー優先度は `Idle` 候補。
- 注意:
  - リカバリーや巨大動画の飢餓を作らない範囲で行う。

### 3.4 レーン予約条件
- リカバリーレーン:
  - `AttemptCount > 0` の待機または実行対象がある時だけ有効。
  - 初期実装では、再試行ジョブを1件だけ先行リースして専用枠へ流しやすくする。
- 低速レーン:
  - `Slow` 判定ジョブが待機または実行対象にある時だけ有効。
  - 初期実装では、通常系とは別に巨大動画を1件だけ先行リースして専用枠へ流しやすくする。
- 通常レーン:
  - 上記の予約成立後の残枠を担当する。

### 3.5 動的並列制御
- 既存の縮退・復帰ロジックは維持する。
- ただし、プリセット起点の解決並列数を `configuredParallelism` として明示する。
- `slow` プリセット時だけ、縮退よりも低負荷維持を優先する余地を設ける。
- 初期実装:
  - `slow` は動的復帰を禁止する。
  - `ballence` / `normal` は復帰に必要な backlog 係数を大きくする。
  - 動的縮退下限はプリセットごとに `2 / 3 / 4` を切り替える。
- 縮退トリガーには、失敗数だけでなく高負荷感知も含める。
- 高負荷感知の候補は CPU、I/O待ち、巨大動画滞留、リカバリ滞留などから評価する。
- 復帰は一気に戻さず、安定期間を見て段階的に行う。

### 3.5.1 第1段階で採用する内部メトリクス
- `ErrorScore`
  - `batchFailedCount`
  - `AutogenTransientFailureCount`
  - `FallbackToFfmpegOnePassCount`
  - `AutogenRetrySuccessCount`
- `QueuePressureScore`
  - `activeCountAfterBatch`
  - `currentParallelism`
  - `configuredParallelism`
- `SlowBacklogScore`
  - `hasSlowDemand`
- `RecoveryBacklogScore`
  - `hasRecoveryDemand`
- `ThroughputPenaltyScore`
  - `batchProcessedCount`
  - `batchMs`
- 第1段階では UI の `PerformanceCounter` 値を使わない。
- 第1段階では Disk温度、UsnMft、SMART を使わない。

### 3.6 高負荷検知の責務分離
- エンジン:
  - 処理時間、失敗理由、再試行回数、デコード失敗などを返す。
- オーケストレータ:
  - エンジン結果と全体負荷情報を統合して並列度を決める。
- 管理者権限サービス:
  - Disk温度、UsnMft、将来のSMART/センサー系情報を収集する。
  - サービス能力を返す。
  - 並列度の最終決定やUI通知文言は持たない。

### 3.7 サービス化前提
- 第1段階:
  - 既存プロセス内で内部メトリクスによる高負荷感知を導入する。
- 第2段階:
  - サービス経由でDisk温度やUsnMft由来の追加シグナルを受け取れるようにする。
- 第3段階:
  - 本体、エンジン、サービスの責務をIPCで疎結合化する。
- 管理者権限サービスは `AdminTelemetry` 単一ホストを前提とし、`UsnMft Status` と `Disk Thermal` をモジュールとして同居させる。

### 3.8 IPC設計方針
- オーケストレータ中心でDTOを定義し、エンジンと管理者権限サービスはそのDTOを介して情報を渡す。
- 最低限必要なDTOは以下とする。
  - `EngineJobMetricsDto`
  - `SystemLoadSnapshotDto`
  - `DiskThermalSnapshotDto`
  - `UsnMftStatusDto`
  - `ThrottleDecisionDto`
- 第1採用のIPC方式は `named pipe + length-prefixed UTF-8 JSON` とする。
- オーケストレータをクライアント、外部化エンジンと管理者権限サービスをサーバーに固定する。
- 管理者権限サービスpipeは `IndigoMovieManager.AdminTelemetry.v1`、エンジンpipeは `IndigoMovieManager.Thumbnail.Engine.v1.{instanceId}` に固定する。
- 接続固定値は `ThumbnailIpcTransportPolicy` へ集約し、初期段階ではプロセス内モックやインターフェースで代替できるようにする。
- サービス未接続時は `SystemLoadSnapshotDto` を内部メトリクス由来で補完する。
- `AdminTelemetryRuntimeResolver` を通して、未接続/未対応/失敗時は `internal-only` スナップショットへ自動移行する。
- `Watcher` 側の `FileIndexProvider` 系IPC/Facadeとは責務を分け、再利用可能な名前付きパイプ基盤や権限サービス基盤のみを共通化する。

### 3.9 DTO実装方針
- DTOは `enum` と `record` を基本にして、意味の曖昧な文字列を避ける。
- `FailureKind`, `DecisionKind`, `ReasonKind`, `ThermalState`, `StatusKind` は列挙型で固定する。
- `CapturedAtUtc` を全DTOへ揃えて、時系列比較可能にする。
- `Nullable` に逃がす項目は最小限にし、未取得理由は状態値で表現する。
- UI向け表示文言はDTOに持たせず、表示変換層で解決する。

### 3.10 high-load 判定実装方針
- 第1段階は内部メトリクスだけで `HighLoadScore` を算出する。
- 第2段階で `DiskThermalSnapshotDto` と `UsnMftStatusDto` を合成入力へ加える。
- 縮退判定は、単発ピークではなく短時間の移動平均で行う。
- 復帰判定は、縮退判定より厳しくしてヒステリシスを持たせる。
- `ThermalState == Critical` は強制縮退、`Warning` は合成スコア加点とする。
- `UsnMftStatusKind == Busy` は `JournalBacklogCount` と `LastScanLatencyMs` を使って I/O圧迫の補助シグナルへ変換する。
- `Io` と `Timeout` の失敗連続は、I/O系高負荷の兆候として扱う。
- `FileIndexProvider` の `availability_error:AdminRequired` などは、サムネイル高負荷とは別軸で扱い、合成スコアへは直接加算しない。
- 係数と閾値は `Settings` で上書き可能にし、実測再調整をノーコードで回せるようにする。

## 4. UI方針
- 設定画面にプリセット選択UIを追加する。
- サムネイル進捗タブ上部にも、同じプリセットを切り替えられるモード選択ドロップを追加する。
- 進捗タブのモード選択ドロップは、設定画面へ潜らずに一時変更できる即時操作導線として扱う。
- 進捗タブで変更した値は `Properties.Settings` へ即時保存し、`ThumbnailProgressRuntime` の表示文言と実行中の制御状態へ同一値を反映する。
- `custum` 時だけ手動並列スライダーを主設定として扱う。
- `idle` の説明文に「最小干渉」、`slow` に「バックグラウンドでゆっくり」、`fast` に「論理コア半分利用」を表示する。
- 現在の解決並列数が、プリセット値か手動値か分かる表示にする。
- 進捗タブでは、現在選択中プリセットと、動的縮退後の実効並列を併記して、表示値と実行値の乖離を見分けられるようにする。

## 5. 移行方針
- 既存ユーザーに `ThumbnailParallelism` の保存値がある場合、初回は `custum` とみなす。
- 明示的にプリセットを選び直した時だけ、自動解決値へ切り替える。
- 既存のレーン閾値は即破棄せず維持する。

## 6. テスト観点
- `slow` 選択時に低負荷モードとして解釈されること。
- `idle` 選択時に最小干渉モードとして解釈されること。
- `fast` 選択時に論理コア半分へ解決されること。
- `custum` 選択時に手動並列数が優先されること。
- リカバリ動画がある時だけリカバリ枠予約が有効になること。
- 巨大動画がある時だけ低速枠制御が有効になること。
- 予約対象が無い時は通常レーンへ全枠を返せること。
- 既存の動的並列縮退・復帰が壊れないこと。
- 高負荷感知時に並列度が抑制されること。
- 負荷回復後に段階的に復帰すること。
- サービス未接続時でも内部メトリクスのみで縮退判断できること。
- サービス接続時にDisk温度等の外部負荷情報を反映できること。
- IPC DTOの欠損項目やタイムアウト時に安全側フォールバックできること。
- 権限不足時にエラー停止せず、`internal-only` 判定へ自動移行できること。
- `HighLoadScore` の境界値で縮退と復帰が暴れないこと。
- `ThermalState == Critical` で即時縮退すること。
- `UsnMftStatusKind == Busy` で backlog / latency が高い時に高負荷スコアへ加点されること。
- `UsnMftStatusKind == Unavailable / AccessDenied` が高負荷スコアへ直接加点されないこと。
- `Io` / `Timeout` 連続失敗で I/O系縮退へ寄ること。
- DTOの時刻差により古いスナップショットを誤採用しないこと。
- `CommonSettingsWindow` の `FileIndexProvider` 3択UIを壊さずにサムネイルUIを追加できること。

## 7. タスクリスト

| ID | 状態 | タスク | 対象 | 完了条件 |
|---|---|---|---|---|
| THR-001 | 完了 | プリセット設定値を追加 | `Properties/Settings.*`, `App.config` | `ThumbnailThreadPreset` が保存できる |
| THR-001A | 完了 | 共有設定画面の既存変更を棚卸しする | `CommonSettingsWindow.xaml`, `CommonSettingsWindow.xaml.cs` | FileIndexProvider 3択UIとの衝突点が把握できる |
| THR-002 | 完了 | プリセット解決ロジックを追加 | `MainWindow.xaml.cs`, `CommonSettingsWindow.xaml.cs` | プリセットから解決並列数を求められる |
| THR-003 | 完了 | 設定UIへプリセット選択を追加 | `CommonSettingsWindow.xaml`, `CommonSettingsWindow.xaml.cs` | UIから切替可能 |
| THR-003A | 未着手 | 進捗タブへモード選択ドロップを追加 | `MainWindow.xaml`, `MainWindow.xaml.cs`, `Thumbnail/ThumbnailProgressRuntime.cs` | 進捗タブから `idle / slow / normal / ballence / fast / max / custum` を切替でき、表示と実行状態が追従する |
| THR-004 | 完了 | `custum` 時の手動並列優先を実装 | `CommonSettingsWindow.xaml.cs`, `MainWindow.xaml.cs` | 既存手動設定と互換維持 |
| THR-005 | 完了 | `slow` 用低負荷モードの実装 | `ThumbnailQueueProcessor.cs` 周辺 | バックグラウンド低負荷動作を実現 |
| THR-005A | 未着手 | `idle` プリセット追加 | 設定/UI/解決ロジック | `idle` を選択・保存・解決できる |
| THR-005B | 未着手 | `idle` 実行モード実装 | `ThumbnailQueueProcessor.cs`, UI更新導線 | `Idle` 優先度相当 + プレビュー抑止 + 更新間引きが動く |
| THR-005C | 未着手 | `idle` 手動回帰追加 | 手動回帰手順 | カーソル引っかかり低減と進捗最低限表示を確認できる |
| THR-006 | 完了 | 需要ベースの予約枠判定を実装 | `ThumbnailQueueProcessor.cs` | 空振り予約を避けつつレーン飢餓を防ぐ |
| THR-007 | 完了 | 既存動的並列制御とプリセット起点の整合調整 | `ThumbnailParallelController.cs`, `ThumbnailQueueProcessor.cs` | プリセットと縮退復帰が両立する |
| THR-007A | 完了 | 高負荷感知トリガーを追加 | `ThumbnailParallelController.cs`, `ThumbnailQueueProcessor.cs` | エラー以外の負荷要因でも抑制できる |
| THR-007B | 未着手 | 高負荷検知の責務分離を整理 | 設計/実装境界 | エンジン・本体・サービスの責務が分離される |
| THR-007C | 完了 | 管理者権限サービス連携前提のI/F案を作る | `Thumbnail/設計メモ_サムネイルサービスIPC構成図_2026-03-06.md`, `src/IndigoMovieManager.Thumbnail.Queue/Ipc/ThumbnailIpcTransportPolicy.cs` | Disk温度/UsnMft情報の受け口を定義できる |
| THR-007D | 完了 | IPC DTOと責務境界を定義 | `src/IndigoMovieManager.Thumbnail.Queue/Ipc/ThumbnailIpcDtos.cs` | メトリクス/負荷/縮退通知のDTOが固まる |
| THR-007E | 完了 | サービス未接続時フォールバックを設計 | `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryRuntimeResolver.cs`, `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs` | 内部メトリクスだけで継続動作できる |
| THR-007F | 完了 | IPC失敗・権限不足時のログ方針を定義 | `Thumbnail/設計メモ_管理者権限テレメトリ劣化ログ方針_2026-03-07.md`, `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryRuntimeResolver.cs`, `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs` | `unavailable`, `access-denied`, `timeout` を区別できる |
| THR-007G | 完了 | DTO列挙型と時刻基準を定義 | `src/IndigoMovieManager.Thumbnail.Queue/Ipc/ThumbnailIpcDtos.cs` | 状態値と時系列比較ルールが固定される |
| THR-007H | 完了 | `HighLoadScore` の係数と閾値を仮実装 | `ThumbnailParallelController.cs` | 縮退と復帰の判定が動く |
| THR-007I | 完了 | 熱暴走優先の強制縮退ルールを追加 | `Thumbnail/ThumbnailParallelController.cs`, `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryRuntimeResolver.cs`, `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs` | 温度危険時に即時減速できる |
| THR-007J | 完了 | IPC構成図と責務図をドキュメント化 | `Thumbnail/設計メモ_サムネイルサービスIPC構成図_2026-03-06.md` | 構成図と通信責務を追跡できる |
| THR-007K | 完了 | 管理者権限サービスの共通化方針を決める | `Thumbnail/設計メモ_共通管理者権限サービス基盤方針_2026-03-07.md`, `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryContracts.cs` | UsnMft と Disk温度で別サービスを作らない |
| THR-007L | 完了 | FileIndexProvider 異常とサムネイル高負荷のログ軸を分離する | `Thumbnail/設計メモ_FileIndexProvider異常とサムネイル高負荷ログ分離_2026-03-07.md`, `Watcher/FileIndexReasonTable.cs`, `Watcher/MainWindow.Watcher.cs` | 可用性異常と負荷異常が混同されない |
| THR-007M | 完了 | UsnMft状態の I/O圧迫シグナル統合を追加 | `Thumbnail/ThumbnailParallelController.cs`, `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryRuntimeResolver.cs`, `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs` | `Busy` と backlog / latency を縮退判断へ反映できる |
| THR-007N | 完了 | high-load 係数と閾値の実測調整設定化を追加 | `Thumbnail/ThumbnailParallelController.cs`, `Properties/Settings.*`, `App.config`, `Thumbnail/実測調整メモ_サムネイル高負荷係数と閾値_2026-03-07.md` | 実機採取後に無改修で係数と閾値を調整できる |
| THR-008 | 完了 | 進捗UIの説明・表示更新 | `ThumbnailProgressRuntime.cs`, UI関連 | レーン意味が分かる |
| THR-009 | 完了 | 単体テスト追加 | `Tests/IndigoMovieManager_fork.Tests/*` | プリセット解決、予約条件、`HighLoadScore`、復帰制御を検証できる |
| THR-010 | 完了 | 手動回帰チェック手順追加 | `Thumbnail/ManualRegressionCheck_サムネイルスレッド制御とレーン設計_2026-03-06.md` | 低負荷・高速・再試行の確認手順がある |

## 8. 段階導入案

### Phase 1
- 設定値追加
- 共有設定UIの棚卸し
- プリセットUI追加
- 並列数解決の導入
- 進捗タブのモード選択ドロップ追加

### Phase 2
- `slow` 低負荷モード導入
- `idle` 最小干渉モード導入
- 需要ベース予約の導入
- 高負荷感知による縮退導入

### Phase 3
- ログ整備
- 進捗UI調整
- テストと手動回帰整備
- 段階的復帰の閾値調整

### Phase 4
- IPC DTO固定
- 管理者権限サービス連携
- 熱情報とUsnMft情報の取り込み
- high-load 判定係数の実測調整

## 9. リスクと対策
- リスク:
  - `slow` の意味が「内部1本固定」と誤解される。
  - 対策:
    - UI文言で「低負荷バックグラウンド」を明示する。
- リスク:
  - `idle` が遅すぎて止まって見える。
  - 対策:
    - `idle` は別モードとして追加し、既存 `slow` を置き換えない。
- リスク:
  - 予約条件が複雑になり挙動が読みにくくなる。
  - 対策:
    - 予約成立理由をログへ出す。
- リスク:
  - 高負荷感知が敏感すぎて常時縮退する。
  - 対策:
    - 感知条件と復帰条件にヒステリシスを設ける。
- リスク:
  - 既存ユーザー設定を壊す。
  - 対策:
    - 既存値は `custum` へ移行する。

## 10. 受け入れ基準
- プリセットをUIから選択できる。
- `idle` が最小干渉モードとして成立する。
- `slow` が低負荷バックグラウンド動作として成立する。
- `fast` が論理コア半分利用の意味で成立する。
- 再試行動画と巨大動画が通常レーンを詰まらせにくくなる。
- 動的並列調整が維持される。
- エラー時または高負荷感知時に抑制し、安定後に復帰できる。
