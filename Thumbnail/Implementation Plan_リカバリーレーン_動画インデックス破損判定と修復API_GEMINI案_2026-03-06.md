# Implementation Plan + tasklist（リカバリーレーン: 動画インデックス破損判定と修復API, 2026-03-06）

## 0. 背景
- 一部の古いFLV等で、インデックス破損により通常レーンのサムネイル作成が失敗する。
- `ffplay` では `Found invalid index entries, clearing the index.` のように、読み込み時に破損インデックスを破棄して再生継続できるケースがある。
- 現行のサムネイル処理は「失敗時の再試行」はあるが、「インデックス破損を検知して修復して再実行」は標準化されていない。
- 将来的に別UIから「動画インデックス修復」を手動実行できるよう、再利用可能なAPI形状で実装しておく必要がある。

## 1. 目的
- リカバリーレーン実行時（再試行ジョブ）に限り、動画インデックス破損判定と修復処理を組み込む。
- 将来のUIからも呼べるよう、修復処理を `ThumbnailCreationService` から利用可能なメソッドとして提供する。
- 既存の通常レーン性能を落とさず、失敗動画の救済率を上げる。

## 2. 要件（確定）
- 対象実行条件:
  - リカバリーレーン実行時のみ有効化する。
  - 現行実装では `AttemptCount > 0` をリカバリー判定として扱う（`ThumbnailQueueProcessor.IsRecoveryLeaseItem`）。
- 判定:
  - 1次判定: FFmpegログ上のインデックス破損シグネチャ検知。
  - 2次判定: `avformat_open_input` / `avformat_find_stream_info` / デコード失敗メッセージの既知シグネチャ補助判定。
- 修復:
  - まず「メモリ上でインデックス非依存読み込み」を試行。
  - 失敗時のみ「再MUX（非再エンコード）」で一時修復ファイルを作成して再実行。
- API:
  - 将来UI向けに、明示的に呼び出せる `Probe` / `Repair` メソッドを公開する。
  - UI側は後続フェーズで追加し、本フェーズではAPIと内部実装までを対象とする。

## 3. スコープ
- IN
  - リカバリーレーン時のインデックス破損判定導入
  - 修復処理（メモリ修復 + 一時再MUX）導入
  - 再利用可能な公開メソッド（Probe/Repair）追加
  - 実行ログ・結果DTO追加
  - 単体テスト追加
- OUT
  - 新規UI画面の追加
  - QueueDBスキーマ変更
  - WhiteBrowser DB（*.wb）の変更

## 4. 設計方針
- 低リスク優先:
  - 既存の通常レーンフローは変更最小。
  - 重い判定・修復はリカバリーレーン時に限定する。
- 責務分離:
  - `ThumbnailCreationService` に「判定/修復オーケストレーション」を置く。
  - 実処理は専用コンポーネントへ切り出し、将来UIから同一APIを再利用できる構造にする。
- 互換性:
  - 既存 `CreateThumbAsync` 呼び出しを壊さないよう、オプション引数追加で拡張する。
- 安全性:
  - 一時修復ファイルは専用テンポラリディレクトリへ作成し、完了後に必ずクリーンアップする。
  - 元動画は不変（上書きしない）をデフォルトとする。

## 5. 追加API（将来UI再利用前提）

### 5.1 追加DTO
- `VideoIndexProbeResult`
  - `bool IsIndexCorruptionDetected`
  - `string DetectionReason`
  - `string ContainerFormat`
  - `bool UsedLogSignature`
  - `bool UsedHeuristic`
- `VideoIndexRepairResult`
  - `bool IsSuccess`
  - `string InputPath`
  - `string OutputPath`
  - `bool UsedTemporaryRemux`
  - `string ErrorMessage`

### 5.2 追加メソッド（ThumbnailCreationService）
- `Task<VideoIndexProbeResult> ProbeVideoIndexAsync(string moviePath, CancellationToken cts = default)`
  - 破損判定のみを行う。
- `Task<VideoIndexRepairResult> RepairVideoIndexAsync(string moviePath, string outputPath, CancellationToken cts = default)`
  - 明示修復（将来UIボタンから呼ぶ想定）。
  - **要件:** `moviePath == outputPath` の指定は例外を投げて完全ブロックする（元動画上書き事故防止）。出力拡張子は `.mp4` や `.mkv` などの安全なコンテナフォーマットを強制する。
- `Task<ThumbnailCreateResult> CreateThumbAsync(...)` のシグネチャ拡張
  - 既存の呼び出し元（引数構成）を壊さないよう、オプション引数として末尾に `bool isRecoveryLane = false` 等を直接追加する（または既存互換のオーバーロードを新設する）。Nullable制約 (`Nullable=disable`) と整合させるため、別DTOクラス（`ThumbnailExecutionOptions?`）の使用は避ける。

### 5.3 実行オプションの扱い（伝搬方法）
- 新規のDTOオブジェクトは作らず、既存のメソッド引数（デフォルト値付き）やコンテキストを利用して、リカバリーレーンかどうかの状態（`isRecoveryLane`）を安全に伝搬させる設計とする。

## 6. 実装詳細

### 6.1 QueueProcessor からのリカバリーフラグ連携
- 現状 `AttemptCount > 0` 判定を維持しつつ、`createThumbAsync` 呼び出し時に `IsRecoveryLane` を伝搬する。
- 伝搬先:
  - `MainWindow.CreateThumbAsync(...)`
  - `ThumbnailCreationService.CreateThumbAsync(...)`

### 6.2 破損判定（Probe）
- FFmpeg.AutoGenのグローバルなログコールバック（`av_log_set_callback`）への依存は**完全に廃止**する（並列実行時の他ジョブ巻き込みリスク排除）。
- 対象コンテナを「破損しやすい形式（FLVなど）」に限定した上で、以下の並列安全なAPI実行・エラーコードベースで判定を行う：
  - `avformat_open_input` の戻り値エラー解析や、明示的なエラーシグネチャによる判定。
  - 映像ストリーム情報の欠落や、ストリーム情報取得失敗などをフックするヒューリスティックな判定。

### 6.3 修復（Repair）
- 第1段:
  - インデックス非依存フラグで読込継続を試行（メモリ修復扱い）。
- 第2段:
  - 一時出力へ再MUX（`-c copy` 相当）して構造修復。
  - サムネ生成は修復済み一時ファイルで再実行。
- 後始末:
  - `KeepTemporaryFixedFile=false` なら最後に一時ファイル削除。

### 6.4 失敗時のフォールバック
- 修復失敗時は既存失敗ハンドリング（Pending/Failed遷移）へ戻す。
- `LastError` に「判定結果」「修復段階」「失敗理由」を集約して記録する。

### 6.5 ログ
- 追加ログキー:
  - `index-probe`
  - `index-repair`
  - `index-repair-summary`
- 出力内容:
  - queue_id / movie_path / is_recovery_lane / detection_reason / repair_stage / result

## 7. タスクリスト

| ID | 状態 | タスク | 対象ファイル | 完了条件 |
|---|---|---|---|---|
| IDX-001 | 未着手 | 実行オプション引数（フラグ）の追加 | `Thumbnail/ThumbnailCreationService.cs` `Thumbnail/MainWindow.ThumbnailCreation.cs` | `isRecoveryLane` などのフラグが伝搬可能になる（DTO新設は中止） |
| IDX-002 | 未着手 | Probe/Repair結果DTOを追加 | `Thumbnail/Engines/VideoIndexProbeResult.cs` `Thumbnail/Engines/VideoIndexRepairResult.cs` | 判定結果と修復結果が Engine プロジェクトから型が見える状態で用意される |
| IDX-003 | 未着手 | `ThumbnailCreationService` に Probe/Repair 公開メソッドを追加 | `Thumbnail/ThumbnailCreationService.cs` | 将来UIから直接呼べるAPIが存在する |
| IDX-004 | 未着手 | `CreateThumbAsync` に実行オプション引数を追加 | `Thumbnail/ThumbnailCreationService.cs` | 既存の呼び出し互換性を破壊せずに拡張できる |
| IDX-005 | 未着手 | QueueProcessor→MainWindow→Service へフラグ伝搬 | `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs` `Thumbnail/MainWindow.ThumbnailCreation.cs` | リカバリー実行時のみ修復経路が有効化される |
| IDX-006 | 未着手 | Probe実装（ログコールバック依存廃止・FLV限定+エラーシグネチャ判定） | `Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs` または `Thumbnail/VideoIndexRepairService.cs` | 破損判定の真偽と理由が安全に返る |
| IDX-007 | 未着手 | Repair実装（メモリ修復→一時再MUX→再実行） | `Thumbnail/ThumbnailCreationService.cs` `Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs` | リカバリーレーン対象で修復後にサムネ作成成功する |
| IDX-008 | 未着手 | 一時ファイル管理とクリーンアップを追加 | `Thumbnail/ThumbnailCreationService.cs` | 例外時含めて一時ファイルリークしない |
| IDX-009 | 未着手 | ログ整備（判定/修復サマリ） | `Thumbnail/ThumbnailCreationService.cs` | 失敗原因追跡がログで可能 |
| IDX-010 | 未着手 | 単体テスト追加（判定・修復・リカバリー限定適用） | `Tests/IndigoMovieManager_fork.Tests/*` | 通常レーン非適用とリカバリー適用が自動検証される |
| IDX-011 | 未着手 | 手動検証手順書を追加 | `Thumbnail/ManualRegressionCheck_インデックス破損修復_2026-03-06.md` | 再現ケースで検証手順が明文化される |

## 8. テスト観点
- 通常レーン（`AttemptCount=0`）:
  - 判定/修復処理が走らないこと。
- リカバリーレーン（`AttemptCount>0`）:
  - 破損判定がONになったときだけ修復処理が走ること。
- 修復成功:
  - サムネ生成が成功し、QueueDBが `Done` になること。
- 修復失敗:
  - 既存ルールで `Pending/Failed` 遷移すること。
- 競合:
  - Probe排他で複数並列時にログコールバック競合を起こさないこと。

## 9. リスクと対策
- リスク: FFmpegログコールバックがグローバルで並列競合する
  - 対策: Probe時の排他ゲートを導入し、対象をリカバリーレーンへ限定する。
- リスク: 一時再MUXがI/Oを圧迫する
  - 対策: リカバリーレーン時のみ実行、通常レーンでは無効。
- リスク: 修復対象拡大で誤判定が増える
  - 対策: 初期はFLV/既知シグネチャ中心で開始し、ログ駆動で拡張する。

## 10. 受け入れ基準
- リカバリーレーン時のみ判定/修復が有効。
- 将来UIから呼べる `ProbeVideoIndexAsync` / `RepairVideoIndexAsync` が存在。
- 既存通常フローの挙動・性能を劣化させない。
- ログで「なぜ修復したか」「どこで失敗したか」を追跡できる。
