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
| `ContainerMetadataBroken` | duration 異常, frame count 不整合 | probe, movie info, engine内の実尺probe | 時刻補正か repair |
| `TransientDecodeFailure` | 一時的 no frames decoded | エンジン失敗 | 再試行 or one-pass 救済 |
| `ShortClipStillLike` | 極短尺, 静止画系, repair後も no frames decoded | 実尺 + 実行結果 | 補助分類して別観測 |
| `NoVideoStream` | video stream missing | probe, エラー文言 | プレースホルダーか最終失敗 |
| `FileLocked` | access denied, in use | IO 例外 | 再試行 |
| `FileMissing` | ファイル消失 | IO 例外 | 最終失敗 |
| `ZeroByteFile` | 0 byte 動画 | ファイル属性 | 最終失敗 |
| `PhysicalCorruption` | EOF, invalid data, decode不能 | 実行結果 | 最終失敗 |
| `HangSuspected` | 難動画で長時間無応答, lease 延長だけ継続 | 実行時間監視, heartbeat 不整合 | 隔離再投入か手動確認 |
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
| `ShortClipStillLike` | 条件付き | いいえ | いいえ | 条件付き | 条件付き |
| `NoVideoStream` | いいえ | いいえ | 条件付き | はい | いいえ |
| `FileLocked` | はい | いいえ | いいえ | 条件付き | いいえ |
| `FileMissing` | いいえ | いいえ | いいえ | はい | いいえ |
| `ZeroByteFile` | いいえ | いいえ | いいえ | はい | いいえ |
| `PhysicalCorruption` | いいえ | 条件付き | いいえ | はい | いいえ |
| `HangSuspected` | 条件付き | いいえ | いいえ | 条件付き | はい |
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

## 5.1 2026-03-10 実動画観測メモ: `No frames decoded`

`<difficult-video-root>` の 17 動画を `AutogenRepairPlaygroundTests` で一括確認した結果、失敗の主流は `No frames decoded` だった。

観測結果の要点:
- 全 17 本中 4 本は全試行失敗、1 本は repair 系のみ成功だった。
- 全試行失敗の 4 本のうち 3 本は、全 failure record が `TransientDecodeFailure + No frames decoded` に集中した。
- 対象:
  - `画像1枚ありページ.mkv`
  - `画像1枚あり顔.mkv`
  - `映像なし_scale_2x_prob-3(1)_scale_2x_prob-3.mkv`
- これらは duration override を変えても改善せず、`repaired-autogen` と `forced-recovery-autogen` でも同じ失敗が続いた。

この 3 本の共通点:
- 実尺が極端に短い
  - 約 `0.033s`
  - 約 `0.069s`
  - 約 `0.98s`
- `ThumbSec=[0]` や `ThumbSec=[1]` でもフレームが取れない
- repair を挟んでも decode 成否が変わらない

設計上の解釈:
- これは「一時的 decode 失敗」というより、短尺/静止画系の `autogen` 非適合に近い。
- 現時点の補助分類名は暫定的に `ShortClipStillLike` とする。
- これは `ContainerMetadataBroken` や `ManualCaptureRequired` に直行させる前の観測用分類として使う。

暫定方針:
- `No frames decoded` でも、極短尺かつ repair 後も不変なものは「通常 retry を増やしても改善しにくい群」とみなす。
- この群は `retryable=true` のまま無制限に混ぜず、`FailureDb` 上では `DetailCode` や `ExtraJson` に
  - `material_duration_sec`
  - `thumb_sec`
  - `repair_attempted`
  - `still_no_frames_after_repair`
  を残す。
- 本番回復方針では、`ffmpeg1pass` への切替条件か `ManualCaptureRequired` 候補として別扱いする。

対照群:
- `35967.mp4` は `original` と `duration-*` では `No frames decoded` だったが、repair 系 4 試行は成功した。
- したがって `No frames decoded` を 1 つに潰さず、
  - repair で回復する群
  - repair でも不変な短尺/静止画群
  に分けて観測する価値がある。

## 5.2 2026-03-11 実装反映メモ: 長尺 `autogen` `No frames decoded`

`35967.mp4` の確認で、次の差が取れた。

- `autogen` は先頭 fallback でも `No frames decoded`
- さらに `ThumbSec=[1200]` の中間 seek 単独試行でも `No frames decoded`
- 一方で `ffmpeg.exe -ss 1200 -frames:v 1` は成功

このため、`35967.mp4` 固有の特例ではなく、次の一般条件を回復方針へ反映した。

- `autogen`
- `No frames decoded`
- 長尺動画
- 手動ではない
- 既知の壊れ入力シグネチャではない

現行実装の扱い:
- 上記条件では、`FailureKind` は `TransientDecodeFailure` のまま維持する。
- ただし回復方針は「初回でも再試行ルーティングだけで止めず、`ffmpeg1pass` 救済を許可する」へ寄せる。
- しきい値は現状 `300秒以上`。

設計上の意図:
- これは `35967.mp4` のファイル名判定ではない。
- 「長尺なのに `autogen` では 1 枚も取れないが、`ffmpeg` 系では回復余地がある」群を救う。
- `ShortClipStillLike` と違い、極短尺専用の補助分類は増やさず、長尺側は `TransientDecodeFailure` の派生回復条件で扱う。

## 5.3 2026-03-11 workthree 短文化受領: `35967型` と `顔型`

workthree 側から、本線へ先に戻す候補が 2 系統に絞られた。

- `35967.mp4 型`
  - `autogen / service` は `No frames decoded`
  - 長尺
  - `ffmpeg midpoint` は成功
  - 本線導入位置は `retry policy`
  - 差し込み位置は `ThumbnailEngineExecutionCoordinator.ApplyPostExecutionFallbacksAsync(...)`
  - 誤適用を避ける代表は `インデックス破壊-093-2-4K.mp4`

- `画像1枚あり顔.mkv 型`
  - `autogen / service` は `No frames decoded`
  - 超短尺
  - 極小 seek `0.001 / 0.01` だけ成功
  - 本線導入位置は `ffmpeg1pass` の短尺 fallback
  - 差し込み位置は `FfmpegOnePassThumbnailGenerationEngine`
  - 誤適用を避ける代表は `画像1枚ありページ.mkv`

今回まだ入れないもの:
- bitrate 閾値の決め打ち
- `FailureKind` 新設
- `画像1枚ありページ.mkv` の救済条件

設計上の扱い:
- `35967型` は既存の `TransientDecodeFailure` の回復条件強化として扱う。
- `顔型` は既存の `ShortClipStillLike` 観測ラインを維持したまま、`ffmpeg1pass` 側の短尺 fallback 条件として扱う。
- どちらも動画名ベタ判定ではなく、`No frames decoded`、尺、seek 成功条件、engine 成否差で一般化する。

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
    ShortClipStillLike,
    NoVideoStream,
    FileLocked,
    FileMissing,
    ZeroByteFile,
    PhysicalCorruption,
    HangSuspected,
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
- `ContainerMetadataBroken` のうち `AVI`/`DIVX` の尺異常は、`ThumbnailJobMaterialBuilder` が `BuildAutoThumbInfo` 直前で実尺probeして補正する。
- `HangSuspected` は engine 例外文字列ではなく、Queue / runtime の時間監視結果から判定する。

### 7.2 Queue
- Queue は `ShouldRetry` と `ShouldFinalizeAsFailed` を見て状態遷移する。
- QueueDB に `FailureKind` を保存するなら、表示と分析目的に限定する。
- Queue がエラー文言を分類し始める設計にはしない。
- Queue は「まだ再試行中か、最終失敗に落ちたか」の境界だけを持ち、途中失敗で固定化しない。
- `HangSuspected` だけは Queue 側の `Leased` / `Running` と heartbeat 監視から補助判定してよい。
- `HangSuspected` を通常の `TransientDecodeFailure` と同じ再試行回数へ混ぜず、隔離レーンか手動確認へ逃がせるようにする。

### 7.3 App
- 失敗一覧の表示、文言、絞り込みに `FailureKind` を使う。
- 手動再試行時は `FailureKind` を消さずに履歴として残す選択もできる。
- `サムネ失敗` タブは QueueDB 直読ではなく、将来はサムネ失敗専用DBを正として `FailureKind` と `Reason` を表示する。
- DebugMode では途中失敗も含めた全失敗を専用DBへ insert し、`FailureKind` の分析母集団にする。

## 8. 優先実装順
1. `FailureKind` enum を Engine 層へ追加する。
2. `ThumbnailPreflightChecker` と失敗プレースホルダー分類を `FailureKind` 返却へ寄せる。
3. `ThumbnailExecutionPolicy` で `Decision` 化する。
4. Queue は `Decision` を使って `Pending / Failed / Done` を更新する。
5. 最後に App の失敗一覧へ `FailureKind` 表示を足す。
6. `Leased` / `Running` 分離後に `HangSuspected` の判定と回復方針を Queue へ接続する。

## 9. 注意点
- `UnsupportedCodec` と `PhysicalCorruption` は似るが、プレースホルダー成功扱いにするか最終失敗にするかが違う。
- `NoVideoStream` は DRM 疑いと真の破損の両方に跨るので、事前判定結果を優先して正規化する。
- `Unknown` を長く残すと分離の価値が落ちるため、ログ集計で早めに細分化する。
- `TransientDecodeFailure` のような再試行系は、失敗のたびに `.#ERROR.jpg` を置くと watcher 再投入を妨げるため、`FinalFail` 確定まで固定化しない。
- `AVI` の shell duration 汚染のように、Queue へ入る前のメタ情報が壊れていても、Engine 側の最終組み立て地点で補正できるようにしておく。
- `HangSuspected` は「例外が取れた失敗」ではなく「返ってこない失敗」なので、engine 文字列分類に寄せず、Queue / runtime の時間軸情報とセットで扱う。
- 難動画では `HangSuspected` と `PhysicalCorruption` が見分けにくいため、初回は `HangSuspected` を暫定分類として保持し、再実行結果で上書きできるようにしておく。
- `No frames decoded` は 1 種類に見えても、2026-03-10 実測では「repair で回復する長尺群」と「repair でも不変な極短尺/静止画群」に分かれた。
- 後者は retry 回数だけ増やしても改善しにくいため、duration と repair 成否を含めて別観測できるようにする。
- `ShortClipStillLike` は主分類を確定し切るための補助観測値であり、当面は `FailureDb` と試験ハーネスから先に導入する。


## 10.1 workthree から本線へ戻す時の受領条件
- 本線側が受け取る最低限の情報は次の5点とする。
  - 動画ごとの失敗理由
  - 成功した条件
  - 再現率
  - 本番導入位置
  - 既存 `FailureKind` で足りるか
- `FailureKind` 観点では、特に次を確認する。
  - 失敗理由が既存の `TransientDecodeFailure`、`ContainerMetadataBroken`、`ShortClipStillLike`、`HangSuspected` のどれで説明できるか。
  - 成功条件が `seek` 調整なのか、`repair` 必須なのか、`engine` 切り替えなのか。
  - 本番導入位置が `preflight`、`retry policy`、`repair workflow`、`finalizer` 前のどこか。
  - 既存 `FailureKind` で吸収できない場合だけ、新設または補助属性追加を検討する。
- 本線へ戻す時は、動画名そのものではなく、失敗パターンと成功条件を一般化した条件で記述する。

## 10.2 `result_signature` の正規化ルール
- `FailureDb.ExtraJson` の `result_signature` は、自由文ではなく比較用の正規化値で持つ。
- 初期の固定候補は次とする。
  - `no-frames-decoded`
  - `near-black`
  - `timeout`
  - `eof`
  - `invalid-data`
  - `index-corruption`
  - `no-video-stream`
  - `file-locked`
  - `file-missing`
  - `unsupported-codec`
  - `drm`
  - `flash`
  - `short-clip-still-like`
  - `unknown`
- `FailureKind` との関係は 1 対 1 固定ではなく、次のように扱う。
  - `no-frames-decoded` は `TransientDecodeFailure` または `ShortClipStillLike`
  - `near-black` は当面 `TransientDecodeFailure` 系の観測値
  - `index-corruption` は `IndexCorruption`
  - `timeout` は `HangSuspected`
- 文字列全文は従来どおり `LastError` や `ResultErrorMessage` に残し、`result_signature` は比較専用とする。

## 10.3 2026-03-11 workthree 優先順位表の受領
- workthree 側で、失敗 9 件の検証順が整理された。
- 本線側ではこの優先順位表を「救済ロジック導入順」ではなく、「観測と一般化の受領順」として扱う。

優先順位:
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

判断基準:
- 件数が多く一般化効果が大きいものを優先
- 既に差分が見えていて再現しやすいものを優先
- 救済不能の疑いが強いものは早めに除外候補として切る

本線側の受け方:
- `P1` は `result_signature / decision_basis / recovery_route` の比較観測を優先する
- `画像1枚あり*` は `ShortClipStillLike` と `ManualCaptureRequired` 境界の検証対象とする
- `near-black` 群は `TransientDecodeFailure` のままにせず、補助分類または補助属性で分離する前提で受ける
- `P2` は `PhysicalCorruption`、`ContainerMetadataBroken`、`TransientDecodeFailure` の切り分けを優先する
- `P3` は本番導入見送り候補を含むため、救済不能の明確化も成果として受け入れる
## 10. 関連文書
- [現状把握_サムネ失敗動画リカバリーフロー_2026-03-09.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/現状把握_サムネ失敗動画リカバリーフロー_2026-03-09.md)
- [調査結果_サムネイルProcessing残留とlease先取り過多_2026-03-10.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/調査結果_サムネイルProcessing残留とlease先取り過多_2026-03-10.md)
- [連絡用doc_サムネ失敗専用DB先行実装_2026-03-10.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/%E9%80%A3%E7%B5%A1%E7%94%A8doc_%E3%82%B5%E3%83%A0%E3%83%8D%E5%A4%B1%E6%95%97%E5%B0%82%E7%94%A8DB%E5%85%88%E8%A1%8C%E5%AE%9F%E8%A3%85_2026-03-10.md)
- [連絡用doc_workthree救済条件の受け皿整理_FailureDbExtraJson_2026-03-11.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/%E9%80%A3%E7%B5%A1%E7%94%A8doc_workthree%E6%95%91%E6%B8%88%E6%9D%A1%E4%BB%B6%E3%81%AE%E5%8F%97%E3%81%91%E7%9A%BF%E6%95%B4%E7%90%86_FailureDbExtraJson_2026-03-11.md)
- [設計整理_FailureDbExtraJson先行反映範囲_2026-03-11.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/%E8%A8%AD%E8%A8%88%E6%95%B4%E7%90%86_FailureDbExtraJson%E5%85%88%E8%A1%8C%E5%8F%8D%E6%98%A0%E7%AF%84%E5%9B%B2_2026-03-11.md)
- [設計メモ_エンジン分離後_サムネ失敗リカバリー責務配置図_2026-03-09.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/設計メモ_エンジン分離後_サムネ失敗リカバリー責務配置図_2026-03-09.md)
- [DCO_エンジン分離実装規則_2026-03-05.md](/c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/DCO_エンジン分離実装規則_2026-03-05.md)
