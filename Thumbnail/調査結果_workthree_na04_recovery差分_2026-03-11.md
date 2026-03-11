# 調査結果 workthree na04 recovery差分 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `E:\_サムネイル作成困難動画\作成1ショットOK\na04.mp4` の現行 `workthree` 挙動を固定する。
- `ラ・ラ・ランド 1_2 / 2_2` と同じ recovery 依存群かを確認する。

## 2. 素性
- `duration=3786.653320`
- `size=57338877`
- `bit_rate=121138`
- `codec_name=h264`
- `pix_fmt=yuv420p`
- `avg_frame_rate=2997/100`
- `nb_frames=113486`

読み取り:
- 長尺 low-bitrate 群だが、`ラ・ラ・ランド` 組よりは bitrate が高い
- それでも現行 `workthree` の bench では非 Recovery だと `No frames decoded` に落ちる

## 3. 結果

### 3.1 Recovery なし
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 失敗 | `No frames decoded` | bench CSV `20260311-114731` |
| `ffmpeg1pass` | 失敗 | `ffmpeg one-pass failed` | 同 CSV 時刻帯 |

### 3.2 Recovery あり
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 成功 | なし | bench CSV `20260311-114748` |
| `ffmpeg1pass` | 失敗 | `ffmpeg one-pass failed` | 同 CSV 時刻帯 |

## 4. 既存履歴との差分
- `thumbnail-create-process.csv` では 2026-03-11 03時台から 11時台まで `autogen success` 履歴が複数ある
- 一方で 2026-03-11 03:58:06 には `No frames decoded` 失敗もある
- 今回の bench では
  - 非 Recovery `autogen = No frames decoded`
  - Recovery あり `autogen = success`
  を再確認した

## 5. 現時点の判断
- `na04` は `35967型` ではなく、`ラ・ラ・ランド 1_2 / 2_2` と同じ recovery 依存寄りに寄せてよい
- つまり `retry policy` より `repair workflow` 側の比較群として扱う方が自然

## 6. 次の一手
1. `shiroka8.mp4`
2. `みずがめ座 (2).mp4`

上の2本へ同じ `Recovery あり / なし` 比較を当て、long no-frames 群のうち repair 寄りがどこまで広がるかを見る

## 7. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_ラ・ラ・ランド1_2_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_ラ・ラ・ランド2_2_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_サムネイル並列再設計向け_難読動画優先順位と成功条件_2026-03-11.md`
