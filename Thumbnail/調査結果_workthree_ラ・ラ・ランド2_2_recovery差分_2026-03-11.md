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

### 5.2 workthree 実運転での成功履歴
| 時刻 | engine | 結果 | 補足 |
| --- | --- | --- | --- |
| `2026-03-11 22:09:11.726` | `opencv` | 成功 | `thumbnail-create-process.csv` 上の最初の成功 |
| `2026-03-11 22:09:59.658` | `ffmpeg1pass` | 成功 | 後続の別試行で単独成功 |

## 6. 既存履歴との差分
- `thumbnail-create-process.csv` では、この動画は 2026-03-11 10時台に
  - `autogen = No frames decoded`
  - `ffmediatoolkit = frame decode failed at sec=2299`
  - `ffmpeg1pass = ffmpeg one-pass failed`
  - `opencv = success`
  の順で最初の成功へ到達していた。
- さらに同日 `22:09:59.658` に `ffmpeg1pass` 単独成功も追加で記録されていた。

## 6.1 Recovery で何が変わるか
- `ThumbnailEngineBenchTests` では `Recovery` 指定時に `QueueObj.AttemptCount = 1` を入れる。
- これにより `ThumbnailRepairExecutionCoordinator.PrepareAsync(...)` 側で recovery lane として扱われる。
- `.mp4` は `ThumbnailExecutionPolicy.IsIndexRepairTargetMovie(...)` の対象拡張子に入るため、`PrepareWorkingMovieAsync(...)` の対象になる。

補足:
- 現行 bench 時刻帯の runtime log では、この個体の `index-repair-summary` 行までは直接拾えていない。
- ただし `1_2` 側では `1_2.remux.mp4` の enqueue 履歴があり、pair として repair 系作業ファイルが使われていた可能性は高い。

読み取り:
- この個体の最初の成功は `opencv` の最終救済で、`autogen` 主成功型ではない。
- ただし同じ個体で後続に `ffmpeg1pass` 成功も出ているため、`opencv` 専用成功ではなく
  `ffmpeg1pass` へ届く条件差分も別途ある。
- したがって、`repair workflow` とエンジン終端フォールバックの両方を比較対象にする価値がある。

## 7. 現時点の判断
- この動画は `35967型` ではない。
  - 理由:
    - `ffmpeg midpoint success` が成立しない
    - `ffmpeg1pass` も単純実行では失敗する
- この動画の workthree 上の本命成功パターンは
  - `autogen -> ffmediatoolkit -> ffmpeg1pass -> opencv`
  の最終 `opencv` 救済成功。
- そのうえで `ffmpeg1pass` 単独成功も後続に出ているので、
  `opencv` を最後の命綱として残しつつ、`ffmpeg1pass` 成功条件の抽出も価値が高い。

## 8. 本線へ返す時の要点
- `retry policy` へ直接一般化する材料にはしない
- `autogen` 成功型としては扱わず、最後に `opencv` が刺さる終端救済型として扱う
- `2_2` では `ffmpeg1pass` 単独成功もあるため、`opencv` 退避だけでなく `ffmpeg1pass` 成功条件の抽出も返却候補に入れる

## 9. 次の一手
1. `1_2` / `2_2` の `opencv` 成功条件を code path とログで固定する
2. `2_2` の後続 `ffmpeg1pass` 成功時の差分条件を抽出する
3. 長尺 low-bitrate / partial 群として、終端フォールバック順をどう本線へ返すか整理する

## 10. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_全動画再試行ベースライン_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_サムネイル並列再設計向け_難読動画優先順位と成功条件_2026-03-11.md`

## 11. 2026-03-12 本線反映後の確定
- 本線 `future` では、次の 3 本を入れたことで `2_2` の実機成功を確認済み。
  - normal 先逃がし
  - recovery 終端 `ffmpeg1pass -> opencv`
  - `Done` 行の再投入巻き戻し防止
- 実機確認:
  - `2026-03-12 21:59:58` normal 開始
  - `2026-03-12 22:00:13` `thumbnail-timeout` / `thumbnail-recovery`
  - rescue 側で同じ `QueueId=10828` を再取得
  - `2026-03-12 22:00:15` `repair success`
- 以後、watch の通常 `Upsert` では `db_affected=0` となり、成功後の再 lease も止まった。
- 完了版の整理は次を正とする。
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\調査結果_ラ・ラ・ランド対策まとめ_2026-03-12.md`
