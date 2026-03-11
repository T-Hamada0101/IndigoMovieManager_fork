# 調査結果 workthree ラ・ラ・ランド2_2 recovery差分 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `E:\_サムネイル作成困難動画\作成1ショットOK\「ラ・ラ・ランド」は少女漫画か！？ 2_2.mp4` の現行 `workthree` 挙動を固定する。
- `retry policy` 側で扱う個体か、`repair workflow` 側で扱う個体かを切り分ける。

## 2. 対象
- 対象動画:
  - `E:\_サムネイル作成困難動画\作成1ショットOK\「ラ・ラ・ランド」は少女漫画か！？ 2_2.mp4`

## 3. 素性
- `ffprobe` 結果
  - `duration=4598.266667`
  - `size=18538700`
  - `bit_rate=32253`
  - `codec_name=h264`
  - `pix_fmt=yuv420p`
  - `avg_frame_rate=30/1`
  - `nb_frames=137948`
- 読み取り
  - 長尺に対してサイズがかなり小さい
  - `35967型` のような「中間1枚抜き可能」個体ではなく、partial / repair 論点を疑う側に寄る

## 4. 実行条件
- 作業ツリー:
  - `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree`
- ビルド:
  - `Debug|x64`
  - 2026-03-11 11:16 台の MSBuild 成功後に実行
- ベンチスクリプト:
  - `Thumbnail\Test\run_thumbnail_engine_bench.ps1`

## 5. 結果

### 5.1 Recovery なし
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 失敗 | `No frames decoded` | bench CSV `20260311-111745` |
| `ffmpeg1pass` | 失敗 | `ffmpeg one-pass failed` | bench CSV `20260311-111805` |

補足:
- `ffmpeg -ss 2299.133 -frames:v 1` の midpoint 直接抽出も失敗した。
- `Nothing was written into output file`、`Could not open encoder before EOF` を確認した。

### 5.2 Recovery あり
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 成功 | なし | bench CSV `20260311-111917`、`output_bytes=21989` |
| `ffmpeg1pass` | 失敗 | `ffmpeg one-pass failed` | 同 CSV 時刻帯 |

## 6. 既存履歴との差分
- `thumbnail-create-process.csv` では、この動画は 2026-03-11 10時台に
  - `Autogen produced a near-black thumbnail`
  が複数回記録されていた。
- 今回の `workthree` bench では
  - Recovery なし `autogen = No frames decoded`
  - Recovery あり `autogen = success`
  へ変わった。

## 6.1 Recovery で何が変わるか
- `ThumbnailEngineBenchTests` では `Recovery` 指定時に `QueueObj.AttemptCount = 1` を入れる。
- これにより `ThumbnailRepairExecutionCoordinator.PrepareAsync(...)` 側で recovery lane として扱われる。
- `.mp4` は `ThumbnailExecutionPolicy.IsIndexRepairTargetMovie(...)` の対象拡張子に入るため、`PrepareWorkingMovieAsync(...)` の対象になる。

補足:
- 現行 bench 時刻帯の runtime log では、この個体の `index-repair-summary` 行までは直接拾えていない。
- ただし `1_2` 側では `1_2.remux.mp4` の enqueue 履歴があり、pair として repair 系作業ファイルが使われていた可能性は高い。

読み取り:
- この個体は `near-black` 単独論点ではなく、入力準備や recovery 有無で入口の症状が変わる。
- したがって、true near-black 群へ混ぜるより、`repair workflow` 側で扱う方が筋が良い。

## 7. 現時点の判断
- この動画は `35967型` ではない。
  - 理由:
    - `ffmpeg midpoint success` が成立しない
    - `ffmpeg1pass` も単純実行では失敗する
- この動画は `repair workflow` 寄りの長尺 low-bitrate / partial 候補として扱う。
  - 理由:
    - Recovery なしでは `autogen` / `ffmpeg1pass` とも失敗
    - Recovery ありでは `autogen` が成功

## 8. 本線へ返す時の要点
- `retry policy` へ直接一般化する材料にはしない
- `repair workflow` で準備済み入力へ変えた時だけ `autogen` が通る長尺群として扱う
- `「ラ・ラ・ランド」は少女漫画か！？ 1_2.mp4` と対で比較し、同じ recovery 依存かを次に確認する

## 9. 次の一手
1. `1_2.mp4` に同じ `Recovery あり / なし` 比較を流す
2. Recovery 時にどの準備経路が効いたかをログで固定する
3. 長尺 low-bitrate / partial 群として `repair workflow` の発火条件へ落とし込めるかを整理する

## 10. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_全動画再試行ベースライン_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_サムネイル並列再設計向け_難読動画優先順位と成功条件_2026-03-11.md`
