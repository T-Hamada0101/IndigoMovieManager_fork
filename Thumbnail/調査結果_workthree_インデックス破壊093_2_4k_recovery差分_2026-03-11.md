# 調査結果 workthree インデックス破壊093 2 4k recovery差分 2026-03-11

最終更新: 2026-03-11

## 1. 対象
- `E:\_サムネイル作成困難動画\作成1ショットOK\インデックス破壊-093-2-4K.mp4`

## 2. ffprobe
- `format_name=mov,mp4,m4a,3gp,3g2,mj2`
- `duration=480.947133`
- `size=481427456`
- `bit_rate=8007989`
- video stream:
  - `codec_name=h264`
  - `width=3840`
  - `height=2160`
  - `avg_frame_rate=30000/1001`
  - `nb_frames=14414`
- audio stream:
  - `codec_name=aac`

## 3. bench結果

### 3.1 非 Recovery
- `autogen`
  - `success=failed`
  - `error_message=No frames decoded`
  - `elapsed_ms=2194`
  - `output_bytes=0`
- `ffmpeg1pass`
  - `success=failed`
  - `error_message=ffmpeg one-pass failed`
  - `elapsed_ms=775`
  - `output_bytes=0`

### 3.2 Recovery あり
- `autogen`
  - `success=success`
  - `elapsed_ms=5938`
  - `output_bytes=6524`
- `ffmpeg1pass`
  - `success=failed`
  - `error_message=ffmpeg one-pass failed`
  - `elapsed_ms=1014`
  - `output_bytes=0`

## 4. 判断
- 非 Recovery では `autogen` / `ffmpeg1pass` とも失敗する。
- Recovery ありでは `autogen` のみ成功し、`ffmpeg1pass` は復帰しない。
- よって `retry policy` 単独で救う個体ではなく、`repair workflow` 側の代表個体として扱うのが妥当である。
- `みずがめ座 (2)` のような Recovery 後に両エンジンが通る群よりは弱く、`na04` / `shiroka8` / `ラ・ラ・ランド` 組に近い。

## 5. 成功条件として固定する項目
- `service_error = No frames decoded`
- `non_recovery_ffmpeg1pass_failed = true`
- `recovery_autogen_success = true`
- `recovery_ffmpeg1pass_failed = true`
- `decision_basis = index-repair-needed + recovery-restores-autogen-only`

## 6. 本線への示唆
- `repair workflow` の代表個体として残す価値が高い。
- `ffmpeg1pass` の one-pass 本線へ単純に逃がしても救えないため、`35967型` や `真空エラー2` 型と混ぜない。
- `PrepareWorkingMovieAsync(...)` で準備済み入力へ寄せた後に `autogen` を通す構図として整理すると、並列再設計の責務分離に合う。

## 7. 参照ログ
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-131547.csv`
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-131612.csv`
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-131637.csv`
