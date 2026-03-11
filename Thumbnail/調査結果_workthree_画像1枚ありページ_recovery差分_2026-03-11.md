# 調査結果 workthree 画像1枚ありページ recovery差分 2026-03-11

最終更新: 2026-03-11

## 1. 対象
- `E:\_サムネイル作成困難動画\画像1枚ありページ.mkv`

## 2. ffprobe
- `format_name=matroska,webm`
- `duration=0.033`
- `size=70817`
- `bit_rate=17167757`
- video stream:
  - `codec_name=h264`
  - `width=1280`
  - `height=720`
  - `avg_frame_rate=30/1`
  - `tags.DURATION=00:00:00.033000000`
- audio stream:
  - `codec_name=aac`
  - `tags.DURATION=00:00:00.000000000`

## 3. bench結果

### 3.1 非 Recovery
- `autogen`
  - `success=failed`
  - `error_message=No frames decoded`
  - `elapsed_ms=1646`
  - `output_bytes=0`
- `ffmpeg1pass`
  - `success=failed`
  - `elapsed_ms=634`
  - `output_bytes=0`

### 3.2 Recovery あり
- `autogen`
  - `success=failed`
  - `error_message=No frames decoded`
  - `elapsed_ms=4419`
  - `output_bytes=0`
- `ffmpeg1pass`
  - `success=failed`
  - `error_message=ffmpeg one-pass failed`
  - `elapsed_ms=593`
  - `output_bytes=0`

## 4. 判断
- `画像1枚あり顔` 型ではない。
- 非 Recovery / Recovery ありの両方で `autogen` / `ffmpeg1pass` が全滅した。
- よって「超短尺なら極小 seek fallback で救う」という一般化をこの個体へ当てるのは誤りである。
- 現時点の `workthree` では `repair workflow` でも改善が見えず、短尺 no-frames の除外候補または別救済候補として分離するのが妥当である。

## 5. 成功条件として固定する項目
- `duration_sec < 0.1`
- `service_error = No frames decoded`
- `non_recovery_ffmpeg1pass_failed = true`
- `recovery_autogen_failed = true`
- `recovery_ffmpeg1pass_failed = true`
- `decision_basis = ultra-short + no-recovery-even-after-repair`

## 6. 本線への示唆
- `画像1枚あり顔` 型の短尺 fallback を本線へ返すときは、この個体を誤適用防止の比較対象として必ずセットで渡す。
- 短尺 no-frames 群でも一括救済はできない。
- 並列再設計では `retry policy` より先に、`preflight` か別救済条件へ切る境界個体として扱う方が安全である。

## 7. 参照ログ
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-131941.csv`
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-132000.csv`
- `C:\Users\{username}\AppData\Local\IndigoMovieManager_fork\logs\thumbnail-engine-bench-20260311-132028.csv`
