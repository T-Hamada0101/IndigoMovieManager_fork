# 設計整理 result_signature と FailureKind 対応表 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `FailureDb.ExtraJson.result_signature` を、本線側でどう `FailureKind` 判断へ使うかを固定する。
- `result_signature` は比較用の正規化値、`FailureKind` は回復方針を伴う主分類として使い分ける。

## 2. 基本方針
- `result_signature` だけで `FailureKind` を即断しない。
- `duration`、`repair` 成否、`preflight_branch`、`engine` 差分を合わせて主分類を決める。
- ただし一次対応表は持ち、受領後の実装位置切り分けを速くする。

## 3. 一次対応表
| result_signature | 第一候補 FailureKind | 補足 |
|---|---|---|
| `no-frames-decoded` | `TransientDecodeFailure` | 長尺で `ffmpeg1pass` 回復余地がある群 |
| `no-frames-decoded` | `ShortClipStillLike` | 1秒以下かつ repair 後も不変な群 |
| `near-black` | `TransientDecodeFailure` | 当面は補助観測。finalizer 前の分岐候補 |
| `timeout` | `HangSuspected` | Queue / runtime 監視と合わせて扱う |
| `eof` | `PhysicalCorruption` | 終端破損寄り |
| `invalid-data` | `PhysicalCorruption` | デコード不能寄り |
| `index-corruption` | `IndexCorruption` | repair 前提 |
| `no-video-stream` | `NoVideoStream` | preflight 結果を優先して正規化 |
| `file-locked` | `FileLocked` | retry 系 |
| `file-missing` | `FileMissing` | final fail 系 |
| `unsupported-codec` | `UnsupportedCodec` | placeholder 候補 |
| `drm` | `DrmProtected` | placeholder 確定 |
| `flash` | `FlashContent` | placeholder 確定 |
| `short-clip-still-like` | `ShortClipStillLike` | 短尺静止画寄り補助分類 |
| `unknown` | `Unknown` | 追加観測が必要 |

## 4. 迷いやすいケース
### 4.1 `no-frames-decoded`
- これだけでは 1 種類に潰さない。
- 次を追加で見る。
  - `material_duration_sec`
  - `repair_attempted`
  - `repair_succeeded`
  - `engine_attempted`
  - `engine_succeeded`
- 長尺で `ffmpeg1pass` が成功するなら `TransientDecodeFailure` の派生回復条件。
- 極短尺で repair 後も不変なら `ShortClipStillLike` 候補。

### 4.2 `near-black`
- 現時点では `TransientDecodeFailure` 系の補助観測として扱う。
- ただし将来、黒画像専用の明確な route が固まるなら別判断軸へ昇格余地あり。

### 4.3 `timeout`
- engine 例外だけでなく、Queue / runtime の lease 情報を合わせて `HangSuspected` へ寄せる。
- 例外文言だけで `PhysicalCorruption` へ落とさない。

## 5. 本線側での使い道
- `FailureDb` の絞り込みと比較
- `FailureKind` 更新要否の判断
- `preflight / retry policy / repair / finalizer` の導入位置切り分け
- 失敗タブでの目視確認

## 6. 今はやらないこと
- `result_signature` から `FailureKind` を機械的に 1 対 1 決め打ちすること
- 既存 `LastError` や `ResultErrorMessage` を捨てること

## 7. 関連
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\設計メモ_FailureKind_失敗分類と回復方針案_2026-03-09.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\Implementation Plan_workthree救済条件の本線受け取りとFailureDbExtraJson標準化_2026-03-11.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\現状把握_FailureDbExtraJsonキー棚卸し_2026-03-11.md`