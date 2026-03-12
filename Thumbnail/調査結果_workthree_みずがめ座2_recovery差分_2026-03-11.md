# 調査結果 workthree みずがめ座2 recovery差分 2026-03-11

最終更新: 2026-03-13

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

## 8. 2026-03-13 future 本線側の追記
- `みずがめ座 (2)` は、通常の多パネル生成では `Object reference not set to an instance of an object.` で落ちる区間がまだ残っている。
- ただし recovery 側に `1秒1枚` の bookmark fallback を追加したことで、repaired movie から代表1枚を取り出して救済できることを確認した。
- 追加対応は2段。
  1. 救済で取れた代表1枚を、要求タブの寸法へ組み直して保存する。
  2. さらに同じ代表1枚から sibling タブ `0/1/2/3/4` の画像も先に横展開しておく。
- 実機ログでは以下の並びを確認した。
  - normal 側で `tab=4` が `NullReference` で失敗
  - rescue 側で `bookmark fallback success: engine=autogen`
  - 続けて `bookmark fallback sibling created: tab=0/1/2/3`
  - その後の通常ジョブは `existing thumbnail reused` で早帰り
- 早帰りが効いたタブ:
  - `160x120x1x1`
  - `56x42x5x1`
  - `200x150x3x1`
  - `120x90x3x1`
- これにより、`みずがめ座 (2)` は
  - rescue で1枚でも取れれば
  - 一般表示タブまで埋めて
  - 後続通常ジョブは既存画像を再利用する
  流れへ持ち込めた。

## 8.1 2026-03-13 future 本線側の追加追記
- 通常 `tab=4` の `NullReference` は、`autogen` 本体の失敗ではなく後始末 `finally` の `bmp.Dispose()` が原因だった。
- `bitmaps` に `null` が混じるケースで cleanup 側の `NullReference` が本来の失敗を覆い隠していたため、`bmp?.Dispose()` に修正した。
- 修正後の実機ログでは、通常 `tab=4` の本当の失敗は `NullReference` ではなく `OperationCanceledException` だった。
  - 停止位置: `TryCaptureFrameBySequentialReadFromFreshContext(...)`
  - 原因: `autogen` の fresh context 逐次読み fallback が normal lane の `10秒` を食い切る
- これに対して、`autogen` の重い逐次読み fallback は `manual / rescue` のみ許可し、通常レーンでは深追いしないようにした。
- 実機体感では、この修正後に `みずがめ座 (2)` の通常レーン応答が明確に速くなった。
  - normal lane は早く rescue へ handoff
  - rescue 側は既存の `1秒1枚 fallback + sibling 作成` で回収
  - 結果として workthree で重視しているテンポ感に近づいた

## 9. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_na04_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_サムネイル並列再設計向け_難読動画優先順位と成功条件_2026-03-11.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md`
