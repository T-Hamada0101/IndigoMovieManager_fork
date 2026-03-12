# 調査結果 workthree みずがめ座2 recovery差分 2026-03-11

最終更新: 2026-03-12

## 1. 目的
- `E:\_サムネイル作成困難動画\作成1ショットOK\みずがめ座 (2).mp4` の現行 `workthree` 挙動を固定する。
- long no-frames 群の中で、Recovery がどこまで効くかを確認する。

## 2. 素性
- `duration=2057.380862`
- `size=255968888`
- `bit_rate=995319`
- `codec_name=h264`
- `pix_fmt=yuv420p`
- `avg_frame_rate=30/1`
- `nb_frames=61721`

読み取り:
- long no-frames 群の中では bitrate がかなり高い
- それでも非 Recovery の bench では `No frames decoded` に落ちる

## 3. 結果

### 3.1 Recovery なし
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 失敗 | `No frames decoded` | bench CSV `20260311-120151` |
| `ffmpeg1pass` | 失敗 | `ffmpeg one-pass failed` | 同 CSV 時刻帯 |

### 3.2 Recovery あり
| engine | 結果 | エラー | 補足 |
| --- | --- | --- | --- |
| `autogen` | 成功 | なし | bench CSV `20260311-120217` |
| `ffmpeg1pass` | 成功 | なし | 同 CSV 時刻帯 |

## 4. 既存履歴との差分
- 過去履歴では `autogen success` と `No frames decoded` が混在していた
- 今回の `workthree` bench では
  - 非 Recovery だと `autogen` / `ffmpeg1pass` とも失敗
  - Recovery ありだと両エンジン成功
  を確認した

## 5. 現時点の判断
- `みずがめ座 (2)` も `repair workflow` 側の個体として扱う
- ただし `ラ・ラ・ランド` 組や `na04` より Recovery 効果が強く、準備後は `ffmpeg1pass` まで成功する
- したがって long no-frames 群は
  - Recovery 後 `autogen` のみ成功
  - Recovery 後 `autogen` / `ffmpeg1pass` の両方成功
  にさらに分かれる可能性がある

## 6. 次の一手
1. `shiroka8.mp4`
2. `真空エラー2_ghq5_temp.mp4`

この2本へ同じ比較を当てて、Recovery 後でも `ffmpeg1pass` が死ぬ群と成功する群の境界を見る

## 7. 2026-03-12 future 本線側の追記
- `future` 側では、`みずがめ座 (2)` を `UnsupportedCodec` placeholder で成功化してしまう誤判定が一度出た。
- 実機ログでは、recovery 中の `ffmpeg1pass` / `opencv` 失敗ログに
  - `Invalid NAL unit size`
  - `missing picture in access unit`
  - `Error splitting the input into NAL units`
  - `Error submitting packet to decoder`
  - `Decoding error`
  が含まれており、`unknown / invalid data found` だけで `CODEC NG` に寄せると誤分類になると分かった。
- これに対して `ThumbnailPlaceholderUtility` を見直し、上記の破損寄り decode error 群は `UnsupportedCodec` へ寄せず、`placeholder-suppressed` として素直に失敗を返すようにした。
- 実機再確認では以下を確認済み。
  - normal lane `15秒 timeout` で rescue へ退避
  - recovery 側で `ffmpeg1pass -> opencv` まで試行
  - 終端は `failure placeholder suppressed`
  - `repair failed`
  - `CODEC NG` は新規生成されない
- さらに、`repair failed` 直後に watcher の通常 `Upsert` が同じ `Failed` 行を `Pending` に戻し、同じ `QueueId` を即再取得するループも見つかった。
- `QueueDbService.Upsert(...)` で `Failed` 行も `Done` 行と同じく「同条件の通常再投入では戻さない」ように修正し、実機で `db_affected=0` と `Status=Failed` 維持まで確認済み。

## 8. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_na04_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_サムネイル並列再設計向け_難読動画優先順位と成功条件_2026-03-11.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md`
