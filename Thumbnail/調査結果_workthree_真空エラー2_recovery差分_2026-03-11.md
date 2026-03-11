# 調査結果 workthree 真空エラー2 recovery差分 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- `E:\_サムネイル作成困難動画\真空エラー2_ghq5_temp.mp4` の現行 `workthree` 挙動を固定する。
- long no-frames 群の中で、`retry policy` 側か `repair workflow` 側かを確定する。

## 2. 素性
- `duration=5.799138`
- `size=30670848`
- `bit_rate=42310906`
- `codec_name=h264`
- `pix_fmt=unknown`
- `avg_frame_rate=120/1`
- `nb_frames=N/A`

読み取り:
- short ではあるが high fps / `pix_fmt=unknown` の特殊個体
- `autogen` では継続して `No frames decoded` を引きやすい

## 3. 結果

### 3.1 Recovery なし
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 失敗 | `No frames decoded` | bench CSV `20260311-124159` |
| `ffmpeg1pass` | 成功 | なし | 同 CSV 時刻帯 |

### 3.2 Recovery あり
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 成功 | なし | 同 CSV 時刻帯 |

## 4. 現時点の判断
- この個体は `repair workflow` 主体ではない
- 非 Recovery の時点で `ffmpeg1pass` が通るので、`retry policy` / one-pass 本線群として扱うのが自然
- Recovery は `autogen` を成功へ寄せる補助にはなるが、本質的な救済主経路は `ffmpeg1pass`

## 5. ここまでの整理
- `35967型`
  - 非 Recovery で `ffmpeg midpoint success`
  - `retry policy`
- `真空エラー2` 型
  - 非 Recovery で `ffmpeg1pass success`
  - `retry policy`
- Recovery 後 `autogen` のみ成功
  - `ラ・ラ・ランド 1_2 / 2_2`
  - `na04`
  - `shiroka8`
- Recovery 後 `autogen` / `ffmpeg1pass` 両方成功
  - `みずがめ座 (2)`

## 6. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_na04_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_shiroka8_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_みずがめ座2_recovery差分_2026-03-11.md`
