# Implementation Plan + tasklist（再考版: Engine分離最優先 / リカバリーレーン動画インデックス修復, 2026-03-06）

## 0. 最優先制約（固定）
- 最優先は **Engineプロジェクトの分離維持**。
- 追加する型・ロジックは、原則 `Thumbnail/Engines/**` 配下へ置く。
- `src/IndigoMovieManager.Thumbnail.Engine` で型解決できない配置（`Thumbnail/*.cs` 新設）は避ける。
- Queue/UI側の変更は最小化し、Engine層へ責務を寄せる。

## 1. 背景
- 一部の古いFLV等で、インデックス破損によりサムネイル作成が失敗する。
- 失敗ジョブは `AttemptCount > 0` でリカバリーレーンに入るため、ここでのみ救済処理を実行するのが低リスク。
- 将来は別UIから「動画インデックス修復」を手動実行したい。

## 2. 目的
- リカバリーレーン実行時のみ、インデックス破損判定と修復を行う。
- 将来UIから再利用できる Probe/Repair メソッドを先に用意する。
- 既存通常レーン性能と既存呼び出し互換を維持する。

## 3. 要件（確定）
- 適用条件:
  - `AttemptCount > 0` のときのみ修復ルートを有効化する。
  - 通常レーン（`AttemptCount == 0`）では現行フローを維持する。
- 判定:
  - `av_log_set_callback` のようなグローバルログコールバックは使わない。
  - エラーコード/既知シグネチャ/ストリーム情報欠落で判定する。
- 修復:
  - 第1段: インデックス非依存読込（メモリ上救済）を試行。
  - 第2段: 一時再MUX（非再エンコード）で修復ファイルを作成して再実行。
- 安全性:
  - `RepairVideoIndexAsync` は `moviePath == outputPath` を禁止する。
  - 出力拡張子は `.mp4` or `.mkv` のみ許可する。
  - 元動画は絶対に上書きしない。

## 4. 配置設計（Engine分離優先）
- 新規配置:
  - `Thumbnail/Engines/IndexRepair/VideoIndexProbeResult.cs`
  - `Thumbnail/Engines/IndexRepair/VideoIndexRepairResult.cs`
  - `Thumbnail/Engines/IndexRepair/IVideoIndexRepairService.cs`
  - `Thumbnail/Engines/IndexRepair/VideoIndexRepairService.cs`
- 既存改修:
  - `Thumbnail/ThumbnailCreationService.cs`（Engineオーケストレーション）
  - `Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs`（必要最小の判定補助）
  - `Thumbnail/MainWindow.ThumbnailCreation.cs`（将来UI呼び出し口の接続準備）
- 方針:
  - QueueProcessorのdelegateシグネチャは変更しない。
  - `QueueObj.AttemptCount` から `isRecoveryLane` を算出してService内で制御する。

## 5. API設計（将来UI再利用）

### 5.1 追加DTO（Engine配下）
- `VideoIndexProbeResult`
  - `bool IsIndexCorruptionDetected`
  - `string DetectionReason`
  - `string ContainerFormat`
  - `string ErrorCode`
- `VideoIndexRepairResult`
  - `bool IsSuccess`
  - `string InputPath`
  - `string OutputPath`
  - `bool UsedTemporaryRemux`
  - `string ErrorMessage`

### 5.2 追加メソッド（ThumbnailCreationService）
- `Task<VideoIndexProbeResult> ProbeVideoIndexAsync(string moviePath, CancellationToken cts = default)`
- `Task<VideoIndexRepairResult> RepairVideoIndexAsync(string moviePath, string outputPath, CancellationToken cts = default)`

### 5.3 既存メソッド互換
- `CreateThumbAsync(...)` の既存シグネチャは維持。
- リカバリーレーン判定は `queueObj.AttemptCount > 0` を使用して内部制御する。
- 将来必要時のみ、互換オーバーロード追加で拡張する（既存呼び出し破壊はしない）。

## 6. 実装フロー

### 6.1 CreateThumbAsync 内フロー
1. `isRecoveryLane = queueObj.AttemptCount > 0` を算出する。
2. `isRecoveryLane == false` なら現行フローをそのまま実行する。
3. `isRecoveryLane == true` なら `ProbeVideoIndexAsync` を実行する。
4. 破損検知時のみ、一時修復ファイルを作って対象パスを差し替える。
5. 既存エンジン順（autogen -> ffmediatoolkit -> ffmpeg1pass -> opencv）で再試行する。
6. 最後に一時ファイルをクリーンアップする（手動修復APIは別扱い）。

### 6.2 手動修復API（将来UI）
1. UIは `RepairVideoIndexAsync(moviePath, outputPath)` を呼ぶ。
2. API側で同一パス禁止・拡張子制限・出力先検証を行う。
3. 成功時は `VideoIndexRepairResult` を返し、UIで案内する。

## 7. タスクリスト

| ID | 状態 | タスク | 対象ファイル | 完了条件 |
|---|---|---|---|---|
| IDX-E001 | 完了 | Engine分離制約に沿って IndexRepair フォルダを新設 | `Thumbnail/Engines/IndexRepair/*` | 新規型が Engine プロジェクトで解決できる |
| IDX-E002 | 完了 | Probe/Repair結果DTOを実装 | `Thumbnail/Engines/IndexRepair/VideoIndexProbeResult.cs` `Thumbnail/Engines/IndexRepair/VideoIndexRepairResult.cs` | 戻り値DTOが確定する |
| IDX-E003 | 完了 | 修復サービス抽象と実装を追加 | `Thumbnail/Engines/IndexRepair/IVideoIndexRepairService.cs` `Thumbnail/Engines/IndexRepair/VideoIndexRepairService.cs` | 判定/修復ロジックがEngine層に閉じる |
| IDX-E004 | 完了 | `ThumbnailCreationService` に Probe/Repair 公開メソッド追加 | `Thumbnail/ThumbnailCreationService.cs` | 将来UIから呼べる公開APIが存在する |
| IDX-E005 | 完了 | `CreateThumbAsync` にリカバリーレーン分岐を追加（シグネチャ維持） | `Thumbnail/ThumbnailCreationService.cs` | `AttemptCount > 0` 時のみ修復ルートが走る |
| IDX-E006 | 完了 | 同一パス禁止・拡張子制限・出力検証を実装 | `Thumbnail/Engines/IndexRepair/VideoIndexRepairService.cs` | 元動画上書き事故を防止できる |
| IDX-E007 | 完了 | 一時修復ファイル運用とクリーンアップを実装 | `Thumbnail/ThumbnailCreationService.cs` | 例外時を含めて一時ファイルが残留しない |
| IDX-E008 | 完了 | ログ整備（probe/repair summary） | `Thumbnail/ThumbnailCreationService.cs` | 失敗理由追跡がログで可能 |
| IDX-E009 | 完了 | 単体テスト追加（通常非適用/リカバリー適用） | `Tests/IndigoMovieManager_fork.Tests/*` | 適用条件と安全制約を自動検証できる |
| IDX-E010 | 完了 | 手動検証手順書を追加 | `Thumbnail/ManualRegressionCheck_インデックス破損修復_2026-03-06.md` | 再現検証手順が確立する |

## 8. テスト観点
- `AttemptCount == 0` ではProbe/Repairが呼ばれないこと。
- `AttemptCount > 0` で破損判定ON時のみ修復が走ること。
- `moviePath == outputPath` 指定時に例外/失敗で即時拒否すること。
- 非許可拡張子出力（例: `.flv`）を拒否すること。
- 修復成功時にサムネ生成が `Done` へ遷移すること。
- 修復失敗時に既存の `Pending/Failed` ルールを維持すること。

## 9. リスクと対策
- リスク: 修復処理のI/O負荷で処理時間が増える
  - 対策: リカバリーレーン限定で実行し、通常レーンには適用しない。
- リスク: 誤判定で不要な再MUXが走る
  - 対策: 対象をFLV等に限定して開始し、ログ観測で拡張判断する。
- リスク: Engine分離が崩れる
  - 対策: 新規型を `Thumbnail/Engines/**` のみに配置する。

## 10. 受け入れ基準
- Engine分離制約を満たしたまま実装できる。
- リカバリーレーン時のみ判定/修復が有効になる。
- 通常レーンの既存フローと性能を維持できる。
- 将来UIが呼べる Probe/Repair API が公開される。
