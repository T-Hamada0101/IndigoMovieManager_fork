# 調査結果 workthree 古い wmv recovery差分 2026-03-11

最終更新: 2026-03-11

## 1. 対象
- `E:\_サムネイル作成困難動画\古い.wmv`

## 2. ffprobe
- `format_name=asf`
- `duration=195.105`
- `size=8369309`
- `bit_rate=343171`
- video stream:
  - `codec_name=wmv3`
  - `width=320`
  - `height=240`
  - `avg_frame_rate=30/1`
- audio stream:
  - `codec_name=wmav2`

## 3. bench結果

### 3.1 非 Recovery
- `autogen`
  - `success=failed`
  - `error_message=No frames decoded`
  - `elapsed_ms=2035`
  - `output_bytes=0`
- `ffmpeg1pass`
  - `success=success`
  - `elapsed_ms=545`
  - `output_bytes=3347`

### 3.2 Recovery あり
- `autogen`
  - `success=success`
  - `elapsed_ms=2332`
  - `output_bytes=54650`
- `ffmpeg1pass`
  - `success=success`
  - `elapsed_ms=695`
  - `output_bytes=54650`

## 4. 判断
- `out1.avi` のような `preflight` / 除外候補ではない。
- 非 Recovery でも `ffmpeg1pass` が通るため、主救済は `retry policy` / one-pass 側である。
- Recovery ありで `autogen` も通るため、repair は補助として効くが主分類は `真空エラー2` 型に寄せるのが自然である。

## 5. 成功条件として固定する項目
- `service_error = No frames decoded`
- `non_recovery_ffmpeg1pass_success = true`
- `recovery_autogen_success = true`
- `decision_basis = autogen-no-frames + non-recovery-ffmpeg1pass-success`

## 6. 本線への示唆
- WMV だから即 `preflight` へ切る判断は危険である。
- 長尺 no-frames 群の中でも、`古い.wmv` は `retry policy` へ流す比較群として扱う。
- `out1.avi` と同じ P3 除外候補からは外し、`真空エラー2` 側の比較群へ寄せる。

## 7. 参照ログ
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-125851.csv`
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-125913.csv`
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-125933.csv`
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-130005.csv`
