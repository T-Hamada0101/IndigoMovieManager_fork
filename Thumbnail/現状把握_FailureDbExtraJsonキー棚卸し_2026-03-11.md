# 現状把握 FailureDbExtraJson キー棚卸し 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- 本線側の `FailureDb.ExtraJson` に現在どのキーが入っているかを整理する。
- `workthree` 受領用に欲しい標準キーとの差分を明確にする。
- 受領後に、どこへ最小追加すべきかを判断しやすくする。

## 2. 現在の書き込み元
### 2.1 Queue 本体
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- `BuildFailureExtraJson(...)`

### 2.2 autogen playground
- `Tests/IndigoMovieManager_fork.Tests/AutogenRepairPlaygroundTests.cs`

### 2.3 ffmpeg 短尺 playground
- `Tests/IndigoMovieManager_fork.Tests/FfmpegShortClipRecoveryPlaygroundTests.cs`

## 3. 現在入っているキー
### 3.1 Queue 本体
- `QueueId`
- `MainDbFullPath`
- `IsRescueRequest`
- `MoviePathKey`
- `ThumbPanelPos`
- `ThumbTimePos`
- `MovieSizeBytes`
- `AttemptCount`
- `AttemptCountAfter`
- `NextStatus`
- `Reason`
- `FailureKindSource`
- `WasRunning`
- `MovieExists`
- `LeaseUntilUtc`
- `StartedAtUtc`
- `ExceptionType`
- `ExceptionMessage`
- `ResultErrorMessage`
- `ResultFailureStage`
- `ResultPolicyDecision`
- `ResultPlaceholderAction`
- `ResultPlaceholderKind`
- `ResultFinalizerAction`
- `ResultFinalizerDetail`

### 3.2 autogen playground
- `AttemptName`
- `IsSuccess`
- `DurationOverrideSec`
- `MaterialDurationSec`
- `ThumbSec`
- `OutputPath`

### 3.3 ffmpeg 短尺 playground
- `AttemptName`
- `EngineId`
- `IsSuccess`
- `DurationOverrideSec`
- `MaterialDurationSec`
- `SeekSec`
- `OutputPath`

## 4. 標準キー案との対応
### 4.1 すでに概ね持っているもの
- `failure_kind_source`
  - 現状: `FailureKindSource`
- `material_duration_sec`
  - 現状: playground 側の `MaterialDurationSec`
- `thumb_sec`
  - 現状: autogen playground 側の `ThumbSec`
- `repair_attempted`
  - 一部 playground では別情報から推定可能だが、固定キーでは未統一
- `repair_succeeded`
  - 同上

### 4.2 まだ足りないもの
- `engine_attempted`
  - Queue 本体では未保持
  - playground では `EngineId` または暗黙の engine 名で保持
- `engine_succeeded`
  - Queue 本体では未保持
- `seek_strategy`
  - どこにも固定キーとして未保持
- `seek_sec`
  - ffmpeg playground にはあるが Queue と autogen では未統一
- `preflight_branch`
  - Queue 本体では未保持
- `result_signature`
  - Queue 本体では `ResultErrorMessage` が近いが、正規化された値ではない
- `repro_confirmed`
  - 未保持
- `recovery_route`
  - 未保持
- `decision_basis`
  - 未保持

### 4.3 既存キーの再利用で吸収できるもの
- `result_signature`
  - `ResultErrorMessage` をそのまま使わず、正規化値を追加すべき
- `engine_attempted`
  - `EngineId` を標準キー名へ寄せれば再利用可能
- `seek_sec`
  - `ThumbSec` / `SeekSec` を共通キーへ寄せれば再利用可能

## 5. 問題点
- Queue 本体と playground でキー命名規則が統一されていない。
- Queue 本体は状態遷移や finalizer 情報に強いが、`engine / seek / repair` の観測が薄い。
- playground は `engine / seek` 観測に強いが、Queue の遷移情報と噛み合わない。
- `result_signature` が未正規化のため、`FailureKind` との対応付けに一段手作業が必要。

## 6. 本線側の最小方針
1. 既存キーは壊さずに残す。
2. 比較軸になる標準キーを追加で足す。
3. Queue 本体では最低限、次を追加候補とする。
   - `engine_attempted`
   - `engine_succeeded`
   - `preflight_branch`
   - `result_signature`
   - `recovery_route`
4. playground 側では最低限、次を追加候補とする。
   - `failure_kind_source`
   - `seek_strategy`
   - `repair_attempted`
   - `repair_succeeded`
   - `repro_confirmed`
5. `ThumbSec` と `SeekSec` は、最終的に `seek_sec` へ寄せる。

## 7. 今すぐ変更しないもの
- 既存 `ExtraJson` キー名の一括リネーム
- 既存保存済みデータの移行
- playground 専用の詳細メモを本線必須キーにすること

## 8. 次の実装候補
- `ThumbnailQueueProcessor.BuildFailureExtraJson(...)` へ標準キーを追記する。
- playground テストの `ExtraJson` も標準キーへ寄せる。
- `FailureDb` 読み出し側で、旧キーと新標準キーの両対応を持つ。
- `result_signature` の正規化規則を `FailureKind` 文書へ追記する。

## 8.1 2026-03-11 本線反映メモ
- Queue 本体の `ExtraJson` へ標準キーを追記済み。
- playground 側の `ExtraJson` も標準キーへ追記済み。
- `サムネ失敗` タブは、旧 PascalCase キーと新 snake_case キーの両方を読める状態へ更新済み。
- `サムネ失敗` タブに `result_signature` と `recovery_route` の部分一致フィルタを追加済み。
- これにより、workthree から戻る受領 JSON は既存保存形式を壊さずに比較可能になった。

## 9. 関連
- `Thumbnail/Implementation Plan_workthree救済条件の本線受け取りとFailureDbExtraJson標準化_2026-03-11.md`
- `Thumbnail/連絡用doc_workthree救済条件の受け皿整理_FailureDbExtraJson_2026-03-11.md`
- `Thumbnail/設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md`
