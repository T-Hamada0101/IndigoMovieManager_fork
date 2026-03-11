# 連絡用doc workthree 画像1枚あり顔 極小seek成功条件 2026-03-11

最終更新: 2026-03-11

## 1. 要点
- `画像1枚あり顔.mkv` は `service` では `No frames decoded`
- ただし `ffmpeg.exe` では、極小 seek の一部だけ成功した
- このため、短尺 no-frames 群の中でも
  - `先頭極小 seek 救済候補`
  として別系統で扱うべき

## 2. 代表ケースで確認できた事実
- 対象:
  - `画像1枚あり顔.mkv`
- duration:
  - `0.069` 秒
- `service`:
  - `No frames decoded`
- `ffmpeg` 中央1枚抜き:
  - 失敗
- 極小 seek:
  - `0.001`: 成功
  - `0.01`: 成功
  - `0.05`: 失敗
  - `0.1`: 失敗
  - `0.25`: 失敗
  - `0.5`: 失敗

## 3. 読み取り
- 「短尺だから全滅」ではない
- `ffmpeg` は、先頭ごく近傍でのみフレーム取得可能
- したがって、短尺救済では
  - `0.001`
  - `0.01`
  を優先候補に入れる価値がある

## 4. 一般化の注意
- 同系統に見える `画像1枚ありページ.mkv` は、同条件で成功していない
- よって現時点では
  - `短尺 no-frames 全体`
  へ一般化しない
- まずは
  - `先頭極小 seek で成功する短尺個体`
  という narrower な群として扱う

## 5. 本線へ返す時の導入候補
- 第一候補:
  - `retry policy`
- 理由:
  - `ffmpeg1pass` の短尺 fallback 候補へ、さらに先頭寄りの seek を足す形が自然

## 6. 本線へ返す時に最低限欲しい記録
- `service_error = No frames decoded`
- `duration_sec < 0.1`
- `short_seek_outcomes`
- `decision_basis = ultra-short + tiny-seek-success`

## 7. 今回はまだ本線へ入れないもの
- `画像1枚ありページ.mkv` を同条件で救う前提
- 短尺群すべてへの一括適用
- `FailureKind` の新設

## 8. 関連
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\調査結果_workthree_優先順自動実行_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\優先順位表_workthree_失敗15件の検証順_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\WorkthreePrioritySequenceTests.cs`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\設計メモ_workthree_画像1枚あり顔_誤適用防止テスト観点_2026-03-11.md`
