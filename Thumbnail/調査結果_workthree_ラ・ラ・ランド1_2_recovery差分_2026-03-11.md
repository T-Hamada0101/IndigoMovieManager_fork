# 調査結果 workthree ラ・ラ・ランド1_2 recovery差分 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `E:\_サムネイル作成困難動画\作成1ショットOK\「ラ・ラ・ランド」は少女漫画か！？ 1_2.mp4` の現行 `workthree` 挙動を固定する。
- `2_2` と同じ recovery 依存群かを確認する。

## 2. 対象
- 対象動画:
  - `E:\_サムネイル作成困難動画\作成1ショットOK\「ラ・ラ・ランド」は少女漫画か！？ 1_2.mp4`

## 3. 素性
- `ffprobe` 結果
  - `duration=4579.833333`
  - `size=12572212`
  - `bit_rate=21960`
  - `codec_name=h264`
  - `pix_fmt=yuv420p`
  - `avg_frame_rate=30/1`
  - `nb_frames=137395`
- 読み取り
  - `2_2` よりさらに low bitrate 寄り
  - 長尺 low-bitrate / partial 候補として扱うのが自然

## 4. 実行条件
- 作業ツリー:
  - `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree`
- ベンチスクリプト:
  - `Thumbnail\Test\run_thumbnail_engine_bench.ps1`
- 実行日時:
  - 2026-03-11 11:32 台

## 5. 結果

### 5.1 Recovery なし
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 失敗 | `No frames decoded` | bench CSV `20260311-113239` |
| `ffmpeg1pass` | 失敗 | `ffmpeg one-pass failed` | 同 CSV 時刻帯 |

補足:
- `ffmpeg -ss 2289.917 -frames:v 1` の midpoint 直接抽出も失敗した。

### 5.2 Recovery あり
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 成功 | なし | bench CSV `20260311-113259` |
| `ffmpeg1pass` | 失敗 | `ffmpeg one-pass failed` | 同 CSV 時刻帯 |

## 6. 既存履歴との差分
- `thumbnail-create-process.csv` では過去に
  - `Autogen produced a near-black thumbnail`
  - `ffmpeg one-pass failed`
  が記録されていた。
- 今回の `workthree` bench では
  - Recovery なし `autogen = No frames decoded`
  - Recovery あり `autogen = success`
  を確認した。

## 6.1 Recovery で何が変わるか
- `ThumbnailEngineBenchTests` では `Recovery` 指定時に `QueueObj.AttemptCount = 1` を入れる。
- これにより `ThumbnailRepairExecutionCoordinator.PrepareAsync(...)` 側で recovery lane として扱われる。
- さらに `.mp4` は `ThumbnailExecutionPolicy.IsIndexRepairTargetMovie(...)` の対象拡張子に入る。
- 実装上は、recovery lane かつ対象拡張子の時だけ `PrepareWorkingMovieAsync(...)` が走る。

補足:
- 現行 bench 時刻帯の runtime log では、この個体の `index-repair-summary` 行までは直接拾えていない。
- ただし 2026-03-11 08:45:11 に `「ラ・ラ・ランド」は少女漫画か！？ 1_2.remux.mp4` の enqueue 履歴があり、repair 系の作業ファイルが作られた痕跡は確認できた。

## 7. 現時点の判断
- `1_2` は `2_2` と同型でよい。
- 2本とも
  - 非 Recovery では `autogen` / `ffmpeg1pass` とも失敗
  - Recovery ありでは `autogen` 成功
  なので、`retry policy` より `repair workflow` 側の個体として扱う。

## 8. 次の一手
1. `1_2` / `2_2` で Recovery 時に効いた準備経路をログで固定する
2. 長尺 low-bitrate / partial 群として repair 発火条件の候補を整理する
3. `na04` など同系候補へ同じ比較を広げる

## 9. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_ラ・ラ・ランド2_2_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_サムネイル並列再設計向け_難読動画優先順位と成功条件_2026-03-11.md`
