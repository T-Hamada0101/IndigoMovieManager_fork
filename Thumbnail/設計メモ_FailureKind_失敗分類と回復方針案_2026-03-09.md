# 設計メモ: FailureKind 失敗分類と回復方針案 2026-03-09

## 1. 目的
- サムネ失敗時の判断を文字列依存から減らし、分離後も使える中立な分類軸を定義する。
- `FailureKind` ごとに、`Retry / Repair / Placeholder / FinalFail / ManualOnly` を明確にする。
- `Queue` と `Engine` の責務境界を固定する。

## 2. 設計方針
- `FailureKind` は Engine が決める。
- Queue は `FailureKind` を直接解釈しなくてもよいが、`retryable` などの派生結果は受け取れるようにする。
- UI は `FailureKind` から表示文言を決めてよいが、分類ロジックは持たない。
- 1つの失敗に対して、最終的な主分類は1つに正規化する。
- 失敗固定用の `.#ERROR.jpg` は途中失敗では置かず、`FinalFail` 確定時だけ出力する。

## 3. FailureKind 案

| FailureKind | 代表例 | 主な判定元 | 第一回復方針 |
|---|---|---|---|
| `None` | 成功 | 実行結果 | なし |
| `DrmProtected` | DRM GUID, PlayReady 系 | 事前判定, エラー文言 | プレースホルダー確定 |
| `FlashContent` | SWF `FWS/CWS/ZWS` | 事前判定 | プレースホルダー確定 |
| `UnsupportedCodec` | decoder not found, unknown codec | エラー文言 | プレースホルダー確定 |
| `IndexCorruption` | seek 失敗, moov/index 異常 | probe, エラー文言 | repair 後再実行 |
| `ContainerMetadataBroken` | duration 異常, frame count 不整合 | probe, movie info | 時刻補正か repair |
| `TransientDecodeFailure` | 一時的 no frames decoded | エンジン失敗 | 再試行 |
| `NoVideoStream` | video stream missing | probe, エラー文言 | プレースホルダーか最終失敗 |
| `FileLocked` | access denied, in use | IO 例外 | 再試行 |
| `FileMissing` | ファイル消失 | IO 例外 | 最終失敗 |
| `ZeroByteFile` | 0 byte 動画 | ファイル属性 | 最終失敗 |
| `PhysicalCorruption` | EOF, invalid data, decode不能 | 実行結果 | 最終失敗 |
| `ManualCaptureRequired` | 自動では位置確定困難 | 運用判断 | 手動対応 |
| `Unknown` | 未分類 | フォールバック | 再試行後に最終失敗 |

## 4. 推奨回復方針テーブル

| FailureKind | Retry | Repair | Placeholder | FinalFail | ManualOnly |
|---|---|---|---|---|---|
| `DrmProtected` | いいえ | いいえ | はい | いいえ | いいえ |
| `FlashContent` | いいえ | いいえ | はい | いいえ | いいえ |
| `UnsupportedCodec` | いいえ | いいえ | はい | いいえ | いいえ |
| `IndexCorruption` | 条件付き | はい | いいえ | 条件付き | いいえ |
| `ContainerMetadataBroken` | 条件付き | はい | いいえ | 条件付き | いいえ |
| `TransientDecodeFailure` | はい | 条件付き | いいえ | 条件付き | いいえ |
| `NoVideoStream` | いいえ | いいえ | 条件付き | はい | いいえ |
| `FileLocked` | はい | いいえ | いいえ | 条件付き | いいえ |
| `FileMissing` | いいえ | いいえ | いいえ | はい | いいえ |
| `ZeroByteFile` | いいえ | いいえ | いいえ | はい | いいえ |
| `PhysicalCorruption` | いいえ | 条件付き | いいえ | はい | いいえ |
| `ManualCaptureRequired` | いいえ | いいえ | いいえ | いいえ | はい |
| `Unknown` | はい | いいえ | いいえ | 条件付き | いいえ |

## 5. 現状実装との対応イメージ

| 現状実装 | 対応させたい FailureKind |
|---|---|
| DRM GUID 事前判定 | `DrmProtected` |
| SWF シグネチャ事前判定 | `FlashContent` |
| `ThumbnailPlaceholderUtility` の codec NG | `UnsupportedCodec` |
| `ThumbnailRepairWorkflowCoordinator` の probe/repair | `IndexCorruption`, `ContainerMetadataBroken` |
| `ThumbnailQueueProcessor` の再試行 | `TransientDecodeFailure`, `FileLocked`, `Unknown` |
| `ThumbnailFailureFinalizer` の error marker | `FinalFail` に落ちた各種の最終失敗固定 |
| Watcher の 0 byte スキップ | `ZeroByteFile` |

## 6. 最小DTO案
```csharp
internal enum FailureKind
{
    None,
    DrmProtected,
    FlashContent,
    UnsupportedCodec,
    IndexCorruption,
    ContainerMetadataBroken,
    TransientDecodeFailure,
    NoVideoStream,
    FileLocked,
    FileMissing,
    ZeroByteFile,
    PhysicalCorruption,
    ManualCaptureRequired,
    Unknown
}

internal sealed class ThumbnailExecutionDecision
{
    public FailureKind FailureKind { get; init; }
    public bool IsSuccess { get; init; }
    public bool ShouldRetry { get; init; }
    public bool ShouldRepair { get; init; }
    public bool ShouldCreatePlaceholder { get; init; }
    public bool ShouldFinalizeAsFailed { get; init; }
    public string DetailCode { get; init; } = "";
}
```

## 7. 実装の寄せ先

### 7.1 Engine
- `ThumbnailPreflightChecker` で `DrmProtected`, `FlashContent` を確定する。
- `ThumbnailExecutionPolicy` で `FailureKind -> Decision` 変換を行う。
- `ThumbnailPlaceholderUtility` は `ShouldCreatePlaceholder` が `true` の時だけ動かす。
- `ThumbnailRepairWorkflowCoordinator` は `ShouldRepair` が `true` の時だけ動かす。
- `ThumbnailFailureFinalizer` は `ShouldFinalizeAsFailed` が `true` の時だけ `.#ERROR.jpg` を出力し、途中再試行では stale マーカーを消す。

### 7.2 Queue
- Queue は `ShouldRetry` と `ShouldFinalizeAsFailed` を見て状態遷移する。
- QueueDB に `FailureKind` を保存するなら、表示と分析目的に限定する。
- Queue がエラー文言を分類し始める設計にはしない。
- Queue は「まだ再試行中か、最終失敗に落ちたか」の境界だけを持ち、途中失敗で固定化しない。

### 7.3 App
- 失敗一覧の表示、文言、絞り込みに `FailureKind` を使う。
- 手動再試行時は `FailureKind` を消さずに履歴として残す選択もできる。

## 8. 優先実装順
1. `FailureKind` enum を Engine 層へ追加する。
2. `ThumbnailPreflightChecker` と失敗プレースホルダー分類を `FailureKind` 返却へ寄せる。
3. `ThumbnailExecutionPolicy` で `Decision` 化する。
4. Queue は `Decision` を使って `Pending / Failed / Done` を更新する。
5. 最後に App の失敗一覧へ `FailureKind` 表示を足す。

## 9. 注意点
- `UnsupportedCodec` と `PhysicalCorruption` は似るが、プレースホルダー成功扱いにするか最終失敗にするかが違う。
- `NoVideoStream` は DRM 疑いと真の破損の両方に跨るので、事前判定結果を優先して正規化する。
- `Unknown` を長く残すと分離の価値が落ちるため、ログ集計で早めに細分化する。
- `TransientDecodeFailure` のような再試行系は、失敗のたびに `.#ERROR.jpg` を置くと watcher 再投入を妨げるため、`FinalFail` 確定まで固定化しない。

## 10. 関連文書
- [現状把握_サムネ失敗動画リカバリーフロー_2026-03-09.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/現状把握_サムネ失敗動画リカバリーフロー_2026-03-09.md)
- [設計メモ_エンジン分離後_サムネ失敗リカバリー責務配置図_2026-03-09.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/設計メモ_エンジン分離後_サムネ失敗リカバリー責務配置図_2026-03-09.md)
- [DCO_エンジン分離実装規則_2026-03-05.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/DCO_エンジン分離実装規則_2026-03-05.md)
