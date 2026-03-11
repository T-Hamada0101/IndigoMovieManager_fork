# 調査結果 workthree 映像なし scale_2x_prob-3 recovery差分 2026-03-11

最終更新: 2026-03-11

## 1. 対象
- `E:\_サムネイル作成困難動画\映像なし_scale_2x_prob-3(1)_scale_2x_prob-3.mkv`

## 2. ffprobe
- `format_name=matroska,webm`
- `duration=0.981`
- `size=17453`
- `bit_rate=142328`
- video stream:
  - `codec_name=h264`
  - `width=1760`
  - `height=940`
  - `avg_frame_rate=30/1`
  - `tags.DURATION=00:00:00.000000000`
- audio stream:
  - `codec_name=aac`

## 3. bench結果

### 3.1 非 Recovery
- `autogen`
  - `success=failed`
  - `error_message=No frames decoded`
  - `elapsed_ms=1682`
  - `output_bytes=0`
- `ffmpeg1pass`
  - `success=failed`
  - `elapsed_ms=491`
  - `output_bytes=0`
  - `error_message=Cannot determine format of input 0:0 after EOF`

### 3.2 Recovery あり
- `autogen`
  - `success=success`
  - `elapsed_ms=3589`
  - `output_bytes=13481`
- `ffmpeg1pass`
  - `success=success`
  - `elapsed_ms=1345`
  - `output_bytes=13481`

## 4. 判断
- `preflight` / 除外候補ではない。
- 非 Recovery では `autogen` / `ffmpeg1pass` とも失敗するが、Recovery ありでは両方成功する。
- したがって主分類は `repair workflow` 側であり、`みずがめ座 (2)` に近い「Recovery 後に両エンジンが通る群」として扱うのが自然である。
- 名前に `映像なし` を含んでも、`ffprobe` 上は video stream が存在するため、ファイル名ベースの `preflight` 除外は危険である。

## 5. 成功条件として固定する項目
- `duration_sec < 1.0`
- `service_error = No frames decoded`
- `non_recovery_ffmpeg1pass_failed = true`
- `recovery_autogen_success = true`
- `recovery_ffmpeg1pass_success = true`
- `decision_basis = short-corrupt-like + recovery-restores-both-engines`

## 6. 本線への示唆
- `preflight` で切るより、`repair workflow` へ送る短尺例外群として扱う方がよい。
- `画像1枚あり顔` 型の極小 seek 成功群とは別で、こちらは Recovery 後に通常の生成が通る短尺 repair 群である。
- 短尺 no-frames を一括で除外すると、この個体を取りこぼす。

## 7. 参照ログ
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-130758.csv`
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-130820.csv`
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-130858.csv`
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-130916.csv`
