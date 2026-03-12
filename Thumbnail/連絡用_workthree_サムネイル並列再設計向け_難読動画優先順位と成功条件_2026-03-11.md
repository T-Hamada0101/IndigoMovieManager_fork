# 連絡用doc workthree サムネイル並列再設計向け 難読動画優先順位と成功条件 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- サムネイル並列方式再設計で、難読動画をどの順で扱うべきかを一本で引き渡す。
- `retry policy`、`repair workflow`、`preflight` のどこに効く論点かを先に揃え、通常系とゆっくり系の責務を崩さずに判断できるようにする。
- 既存の個別メモに散っている「成功した条件」と「まだ本線へ返さない条件」を集約する。

## 2. 先に結論
- 本線へ返す優先度は次の順でよい。
  1. `35967型`
  2. `画像1枚あり顔` 型
  3. true near-black 2件の再比較
  4. `インデックス破壊-093-2-4K.mp4`
  5. `画像1枚ありページ.mkv`
  6. 除外候補 3件
- 並列再設計の観点では、`35967型` と `画像1枚あり顔` 型が最重要である。
- 理由は、どちらも「通常系で初回 `autogen` を試し、失敗後はゆっくり系の救済へ渡す」という再設計方針にそのまま接続できるからである。
- `35967型` は 2026-03-11 の再確認でも、非 Recovery で `autogen = No frames decoded`、`ffmpeg1pass = success`、`ffmpeg midpoint success` を維持した。

## 3. 優先順位表
| 優先 | 対象群 | 現在の状態 | 成功条件の核 | 主な差し込み先 | 並列再設計への意味 |
| --- | --- | --- | --- | --- | --- |
| P1 | `35967型` | `autogen / service = No frames decoded`、`ffmpeg` 中間1枚抜き成功 | 長尺、`ffmpeg midpoint success`、推定 bitrate は極端に低くない | `retry policy` | 通常系で粘らず、ゆっくり系 `ffmpeg1pass` に落とす代表条件 |
| P1 | `画像1枚あり顔` 型 | `autogen / service = No frames decoded`、中央 seek 失敗、極小 seek のみ成功 | `duration_sec < 0.1`、`0.001 / 0.01` 秒 seek 成功 | `retry policy` または `FfmpegOnePassThumbnailGenerationEngine` | 超短尺救済を通常系に居座らせず、ゆっくり系の短尺 fallback として分離できる |
| P1 | true near-black 2件 | `Autogen produced a near-black thumbnail` 固定 | `EOFドレイン` 取り込み後も near-black が残るか、古い暗フレーム捨てで改善するか | `finalizer` 前 または `retry policy` | `No frames decoded` 群と混ぜず、黒判定ロジックだけを別論点で扱える |
| P1 | `インデックス破壊-093-2-4K.mp4` | 非 Recovery では `autogen` / `ffmpeg1pass` とも失敗、Recovery ありで `autogen` のみ成功 | `recovery_autogen_success` と `recovery_ffmpeg1pass_failed` の同居 | `repair workflow` | これは救済レーンでも `retry policy` 単独では足りない代表個体 |
| P2 | `画像1枚ありページ.mkv` | `画像1枚あり顔` に似るが、非 Recovery / Recovery ありの両方で失敗 | `ultra-short + no-recovery-even-after-repair` | `preflight` または別救済条件 | 短尺だから一括救済、を防ぐための誤適用防止役 |
| P2 | 長尺 no-frames 群 | `真空エラー2`、`古い.wmv`、`ラ・ラ・ランド`、`na04`、`shiroka8`、`みずがめ座` など | one-pass / repair の効き方を個体差込みで確認 | `retry policy` または `repair workflow` | `35967型` の一般化限界を見極める比較群。`真空エラー2` と `古い.wmv` は非 Recovery でも `ffmpeg1pass` 成功のため retry policy 寄り。`ラ・ラ・ランド 1_2 / 2_2`、`na04`、`shiroka8` は Recovery あり `autogen` 成功、`みずがめ座 (2)` は Recovery ありで `autogen` / `ffmpeg1pass` 両方成功のため repair 寄り |
| P2 | 短尺 repair 群 | `映像なし_scale_2x_prob-3(1)_scale_2x_prob-3.mkv` | 非 Recovery では両方失敗、Recovery ありで両方成功 | `repair workflow` | 短尺 no-frames を `preflight` で切らないための比較群 |
| P3 | 除外候補 1件 | `out1.avi` | 実フレーム不能かを切る | `preflight` | 通常系にもゆっくり系にも流さない境界条件の確定 |

## 4. 成功条件

### 4.1 `35967型`
- 判定に使う主条件
  - `service_error = No frames decoded`
  - 長尺動画
  - `ffmpeg_midpoint_success = true`
  - `ffmpeg1pass_success = true`
- 補助条件
  - `estimated_bitrate_kbps` が極端に低くない
- 成功条件として固定したい記録
  - `duration_sec`
  - `midpoint_sec`
  - `ffmpeg_midpoint_success = true`
  - `ffmpeg1pass_success = true`
  - `decision_basis = autogen-no-frames + ffmpeg-midpoint-success + ffmpeg1pass-success`
- 並列再設計での扱い
  - 初回は通常系 `autogen`
  - 失敗後は通常系で引きずらず、ゆっくり系 `ffmpeg1pass` 候補へ落とす

### 4.2 `画像1枚あり顔` 型
- 判定に使う主条件
  - `service_error = No frames decoded`
  - `duration_sec < 0.1`
  - `ffmpeg` 極小 seek `0.001 / 0.01` だけ成功
- 成功条件として固定したい記録
  - `short_seek_outcomes`
  - `decision_basis = ultra-short + tiny-seek-success`
- 並列再設計での扱い
  - `EOFドレイン` で救えない残件を、ゆっくり系の短尺 fallback として処理する
  - `画像1枚ありページ.mkv` に誤適用しないことを前提にする

### 4.2a `画像1枚ありページ` 型
- 判定に使う主条件
  - `duration_sec < 0.1`
  - 非 Recovery では `autogen = No frames decoded`
  - 非 Recovery では `ffmpeg1pass failed`
  - Recovery ありでも `autogen` / `ffmpeg1pass` が両方失敗
- 成功条件として固定したい記録
  - `recovery_autogen_failed = true`
  - `recovery_ffmpeg1pass_failed = true`
  - `decision_basis = ultra-short + no-recovery-even-after-repair`
- 並列再設計での扱い
  - `画像1枚あり顔` 型の誤適用防止役
  - `preflight` または別救済条件に分ける比較群として扱う

### 4.3 true near-black 2件
- 判定に使う主条件
  - `Autogen produced a near-black thumbnail`
  - `EOFドレイン` 取り込み後も症状が残る
- 成功条件として固定したい記録
  - `P1B-01 = E:\_サムネイル作成困難動画\_steph_myers_-1836566168414388686-20240919_094110-vid1.mp4`
  - `P1B-02 = E:\_サムネイル作成困難動画\作成NG\【ライブ配信】神回ですか！？な おP様 配信！！_scale_2x_prob-3.mp4`
  - 黒判定しきい値
  - 代表フレーム選定条件
  - `ffmpeg1pass` へ逃がした時の差分
- 並列再設計での扱い
  - これはレーン振り分けより、`autogen` 側の採用フレーム品質の論点
  - `No frames decoded` 群と同じルールへ混ぜない
  - `true-near-black-pair-summary-latest.csv` 上では 2件とも near-black が維持されている

### 4.4 `再生できない.flv`
- いま固定できている成功条件
  - `movieinfo-probe` で `autogen success=True`
  - `duration_sec=478.95` 前後、`fps=19.938`、`frames=9549`、`codec='flv1'`
  - `thumbnail-create-process.csv` に `autogen success`
  - worker log に `thumbnail done role=Normal`
- 並列再設計での意味
  - 拡張子や codec だけで、ゆっくり系や repair へ送らない
  - `movieinfo-probe` が成立する個体は通常系 `autogen` 直通候補に残す

### 4.5 `ラ・ラ・ランド 2_2` 型
- 判定に使う主条件
  - 長尺
  - 極端な low bitrate / partial 疑い
  - Recovery なしでは `autogen` / `ffmpeg1pass` とも失敗
  - Recovery ありでは `autogen` が成功
- 成功条件として固定したい記録
  - `is_recovery = 1`
  - `autogen success`
  - `ffmpeg1pass failed`
  - midpoint 直接抽出失敗
- 並列再設計での扱い
  - `retry policy` へ単純に逃がす個体ではなく、`repair workflow` で準備済み入力へ寄せる候補
  - `35967型` と混ぜない

### 4.6 `na04` 型
- 判定に使う主条件
  - 長尺
  - 非 Recovery では `autogen = No frames decoded`
  - Recovery ありでは `autogen = success`
  - `ffmpeg1pass` は両方で失敗
- 成功条件として固定したい記録
  - `is_recovery = 1`
  - `autogen success`
  - `ffmpeg1pass failed`
- 並列再設計での扱い
  - `retry policy` より `repair workflow` 側の比較群
  - `ラ・ラ・ランド` 組と同じ recovery 依存候補として扱う

### 4.7 `みずがめ座 (2)` 型
- 判定に使う主条件
  - 長尺
  - 非 Recovery では `autogen = No frames decoded`
  - Recovery ありでは `autogen` / `ffmpeg1pass` の両方成功
- 成功条件として固定したい記録
  - `is_recovery = 1`
  - `autogen success`
  - `ffmpeg1pass success`
- 並列再設計での扱い
  - `retry policy` ではなく `repair workflow` 側
  - Recovery 後に両エンジンが通る群として、`na04` や `ラ・ラ・ランド` 組と分けて見る

### 4.8 `shiroka8` 型
- 判定に使う主条件
  - 非 Recovery では `autogen = No frames decoded`
  - Recovery ありでは `autogen success`
  - `ffmpeg1pass` は Recovery 後も失敗
- 成功条件として固定したい記録
  - `is_recovery = 1`
  - `autogen success`
  - `ffmpeg1pass failed`
- 並列再設計での扱い
  - `repair workflow` 側
  - `na04` と同型の比較群として扱う

### 4.9 `真空エラー2` 型
- 判定に使う主条件
  - 非 Recovery では `autogen = No frames decoded`
  - 非 Recovery でも `ffmpeg1pass success`
  - Recovery は補助であって主救済ではない
- 成功条件として固定したい記録
  - `ffmpeg1pass success`
  - `pix_fmt=unknown`
  - `avg_frame_rate=120/1`
- 並列再設計での扱い
  - `retry policy` / one-pass 本線側
  - `repair workflow` 主体の群と混ぜない

### 4.10 `古い.wmv` 型
- 判定に使う主条件
  - 非 Recovery では `autogen = No frames decoded`
  - 非 Recovery でも `ffmpeg1pass success`
  - Recovery ありでは `autogen success`
- 成功条件として固定したい記録
  - `format_name=asf`
  - `codec_name=wmv3`
  - `non_recovery_ffmpeg1pass_success = true`
  - `recovery_autogen_success = true`
- 並列再設計での扱い
  - `retry policy` / one-pass 本線側
  - WMV だから即 `preflight` へ切らない比較群として扱う

### 4.11 `映像なし...mkv` 型
- 判定に使う主条件
  - `duration_sec < 1.0`
  - 非 Recovery では `autogen = No frames decoded`
  - 非 Recovery では `ffmpeg1pass failed`
  - Recovery ありでは `autogen` / `ffmpeg1pass` の両方成功
- 成功条件として固定したい記録
  - `tags.DURATION=00:00:00.000000000`
  - `recovery_autogen_success = true`
  - `recovery_ffmpeg1pass_success = true`
  - `decision_basis = short-corrupt-like + recovery-restores-both-engines`
- 並列再設計での扱い
  - `repair workflow` 側
  - 短尺 no-frames を名前や先入観だけで `preflight` 除外しない比較群として扱う

## 5. 本線へ渡す判断

### 5.1 すぐ渡してよい
- `35967型`
- `画像1枚あり顔` 型

### 5.2 まだ比較継続
- true near-black 2件
- `インデックス破壊-093-2-4K.mp4`
- 長尺 no-frames 群のうち `ラ・ラ・ランド` 以外
- `古い.wmv`
- `映像なし_scale_2x_prob-3(1)_scale_2x_prob-3.mkv`

### 5.3 先に除外判定を固める
- `out1.avi`
- `画像1枚ありページ.mkv`

補足:
- `out1.avi` は現行 `workthree` で Recovery ありでも `autogen` / `ffmpeg1pass` とも失敗したため、先に `preflight` 側へ寄せる判断が妥当
- `画像1枚ありページ.mkv` は超短尺でも Recovery あり / なしの両方で `autogen` / `ffmpeg1pass` が全滅したため、`画像1枚あり顔` 型の短尺 fallback から外し、`preflight` または別救済条件側へ寄せる
- `古い.wmv` は非 Recovery でも `ffmpeg1pass` が通り、Recovery ありで `autogen` も通ったため、除外候補から外して `retry policy` 比較群へ寄せる
- `映像なし...mkv` はファイル名に反して video stream があり、Recovery ありで `autogen` / `ffmpeg1pass` が両方通ったため、除外候補から外して短尺 repair 群へ寄せる

## 6. サムネイル並列再設計への落とし込み
- 通常系
  - 初回 `autogen` を担当する
  - `movieinfo-probe` が成立する FLV 系や通常動画はここで回す
- ゆっくり系
  - `35967型` のような長尺 `No frames decoded` 群の `ffmpeg1pass`
  - `画像1枚あり顔` 型のような超短尺 fallback
  - repair 前提個体
- `preflight`
  - 明らかな除外候補を、通常系にもゆっくり系にも流さない

## 7. 引き渡し時の最低セット
1. `35967型` を `retry policy` 候補として共有する
2. `画像1枚あり顔` 型を短尺 fallback 候補として共有する
3. true near-black 2件は `P-04` 観点の比較継続だと明記する
4. `画像1枚ありページ.mkv` は誤適用防止の比較対象として残す
5. 除外候補は `out1.avi` を中心に `preflight` 側の宿題として切り出す

## 8. 参照
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\仕様書_サムネイル並列方式再設計_2026-03-08.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_本線引き継ぎ_難読動画成果要約_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_35967型判定基準と本線反映候補_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_35967_retry_policy再確認_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_画像1枚あり顔_極小seek成功条件_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_再生できないflv_成功条件_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_古い_wmv_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_画像1枚ありページ_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_インデックス破壊093_2_4k_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_映像なし_scale_2x_prob-3_recovery差分_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_true_near_black_2件固定_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\調査結果_難読動画成功パターン集_2026-03-11.md`
