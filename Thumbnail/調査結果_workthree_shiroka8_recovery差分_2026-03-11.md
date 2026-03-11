# 調査結果 workthree shiroka8 recovery差分 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `E:\_サムネイル作成困難動画\作成1ショットOK\shiroka8.mp4` の現行 `workthree` 挙動を固定する。
- long no-frames 群のどの型へ入るかを切る。

## 2. 素性
- `duration=334.016000`
- `size=44957696`
- `bit_rate=1076779`
- `codec_name=h264`
- `pix_fmt=yuv420p`
- `avg_frame_rate=3030300/101111`
- `nb_frames=10010`

読み取り:
- long 群の中では短め
- bitrate は比較的高いが、非 Recovery の bench では `No frames decoded` へ落ちる

## 3. 結果

### 3.1 Recovery なし
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 失敗 | `No frames decoded` | bench CSV `20260311-122657` |
| `ffmpeg1pass` | 失敗 | `ffmpeg one-pass failed` | 同 CSV 時刻帯 |

### 3.2 Recovery あり
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 成功 | なし | bench CSV `20260311-122717` |
| `ffmpeg1pass` | 失敗 | `ffmpeg one-pass failed` | 同 CSV 時刻帯 |

## 4. 既存履歴との差分
- 過去履歴では `autogen success` が多い
- ただし playground 系では `No frames decoded` 失敗履歴もある
- 今回の bench では
  - 非 Recovery 失敗
  - Recovery あり `autogen` 成功
  を確認した

## 5. 現時点の判断
- `shiroka8` は `na04` と同型でよい
- つまり
  - `repair workflow` 寄り
  - Recovery 後 `autogen` は通る
  - `ffmpeg1pass` は通らない
  群である

## 6. ここまでの long no-frames 群の整理
- Recovery 後 `autogen` のみ成功
  - `ラ・ラ・ランド 1_2 / 2_2`
  - `na04`
  - `shiroka8`
- Recovery 後 `autogen` / `ffmpeg1pass` 両方成功
  - `みずがめ座 (2)`
- `35967型`
  - 非 Recovery でも `ffmpeg midpoint success`
  - `retry policy` 側

## 7. 次の一手
1. `真空エラー2_ghq5_temp.mp4`
2. `out1.avi`

`repair workflow` 側の追加代表と、除外・別群候補を1本ずつ確認すると全体像が締まる

## 8. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_na04_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_みずがめ座2_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_サムネイル並列再設計向け_難読動画優先順位と成功条件_2026-03-11.md`
