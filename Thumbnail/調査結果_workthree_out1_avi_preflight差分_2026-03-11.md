# 調査結果 workthree out1 avi preflight差分 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `E:\_サムネイル作成困難動画\作成OK\out1.avi` を、救済対象に残すべきか、`preflight` / 除外寄りへ寄せるべきかを確認する。

## 2. 素性
- `duration=29742.068390`
- `size=6338030`
- `bit_rate=1704`
- `codec_name=h264`
- `pix_fmt=yuv420p`
- `avg_frame_rate=29971/1000`
- `nb_frames=2400`

読み取り:
- duration に対して size / bitrate が極端に小さい
- long low-bitrate / partial 疑いが非常に強い

## 3. 結果

### 3.1 Recovery なし
| engine | 結果 | エラー |
| --- | --- | --- |
| `autogen` | 失敗 | `No frames decoded` |
| `ffmpeg1pass` | 失敗 | `ffmpeg one-pass failed` |

bench CSV:
- `20260311-124441`

### 3.2 Recovery あり
| engine | 結果 | エラー |
| --- | --- | --- |
| `autogen` | 失敗 | `No frames decoded` |
| `ffmpeg1pass` | 失敗 | `ffmpeg one-pass failed` |

bench CSV:
- `20260311-124501`

## 4. 既存履歴との差分
- 過去履歴では `autogen success` も混じっていた
- ただし現行 `workthree` の bench 条件では
  - Recovery なしでも失敗
  - Recovery ありでも失敗
  で固定した

## 5. 現時点の判断
- この個体は `repair workflow` で粘る代表ではない
- まず `preflight` / 除外候補として扱い、通常の救済群から外す方がよい
- 少なくとも現行 `workthree` では
  - `retry policy`
  - `repair workflow`
  のどちらへ寄せても改善が見えていない

## 6. 次の一手
1. `古い.wmv`
2. `映像なし_scale_2x_prob-3(1)_scale_2x_prob-3.mkv`

除外候補 3件を揃えて、`preflight` 側へ切る条件をまとめる

## 7. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_サムネイル並列再設計向け_難読動画優先順位と成功条件_2026-03-11.md`
