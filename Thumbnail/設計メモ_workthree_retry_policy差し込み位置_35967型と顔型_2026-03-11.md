# 設計メモ workthree retry policy差し込み位置 35967型と顔型 2026-03-11

最終更新: 2026-03-11

## 1. 目的
- 本線側の `retry policy` 実コードに対して、
  - `35967.mp4 型`
  - `画像1枚あり顔.mkv` 型
  の差し込み位置を整理する。
- 実装前に、どこを変えるかと、どこは変えないかを固定する。

## 2. 現在の差し込み候補

### 2.1 判定ロジック側
- 対象:
  - `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\ThumbnailExecutionPolicy.cs`
- 現在の関数:
  - `ShouldTryRecoveryOnePassFallback(...)`
  - `ShouldTryInitialLongClipOnePassFallback(...)`

### 2.2 実行ロジック側
- 対象:
  - `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\ThumbnailEngineExecutionCoordinator.cs`
- 現在の関数:
  - `ApplyPostExecutionFallbacksAsync(...)`
- 現状の流れ:
  - `ShouldTryRecoveryOnePassFallback(...) == true`
  - なら `ffmpeg1pass` を 1 回実行する

## 3. `35967.mp4 型` の差し込み位置

### 3.1 判定
- 第一候補:
  - `ShouldTryInitialLongClipOnePassFallback(...)`
- 理由:
  - 既に「初回 + 長尺 + autogen no-frames」で `ffmpeg1pass` に落とす入口がある
  - `35967.mp4 型` はここを精密化する方が最小差分

### 3.2 追加したい判定軸
- 既存:
  - 長尺
  - `[autogen] no frames decoded`
- 追加候補:
  - `ffmpeg midpoint success`
- 補助情報:
  - `estimated bitrate`

### 3.3 実装上の注意
- `ThumbnailExecutionPolicy` だけでは `ffmpeg midpoint success` を持てない
- したがって実装は次のどちらかになる
  1. `ApplyPostExecutionFallbacksAsync(...)` 側で限定 probe を実行し、その結果を見て `ffmpeg1pass` 実行へ進む
  2. probe 結果を DTO 化して `ThumbnailExecutionPolicy` へ渡す

### 3.4 推奨
- まずは `ApplyPostExecutionFallbacksAsync(...)` 側で限定 probe
- 理由:
  - `35967.mp4 型` は `retry policy` の post-process 判断に近い
  - `ThumbnailExecutionPolicy` を probe 実装で汚さずに済む

## 4. `画像1枚あり顔.mkv` 型 の差し込み位置

### 4.1 判定
- 第一候補:
  - `ffmpeg1pass` エンジン内部
- 対象:
  - `C:\Users\{username}\source\repos\IndigoMovieManager_fork\Thumbnail\Engines\FfmpegOnePassThumbnailGenerationEngine.cs`
- 既存:
  - `ffmpeg1pass-shortclip-seek`
  - 短尺 fallback 候補を既に持っている

### 4.2 理由
- `顔.mkv型` は
  - `autogen no-frames`
  - 超短尺
  - 極小 seek だけ成功
  というパターン
- これは `retry policy` より
  - `ffmpeg1pass` の短尺 seek 候補設計
  に近い

### 4.3 追加したい変更
- 既存の短尺 seek 候補列へ
  - `0.001`
  - `0.01`
  を優先位置で明示するか再検証する
- ただし
  - `画像1枚ありページ.mkv`
  に誤適用しないよう、成功条件の観測は別途必要

## 5. 変えない方がよい場所
- `preflight`
  - `35967型` のために前段で毎回 midpoint probe を入れるのは重い
- `finalizer`
  - 救済のタイミングとして遅い
- `FailureKind`
  - 今回は新設しない

## 6. 実装順の提案
1. `35967型`
   - `ApplyPostExecutionFallbacksAsync(...)` で限定 midpoint probe を差し込む案を先に試す
2. `顔.mkv型`
   - `FfmpegOnePassThumbnailGenerationEngine` の短尺候補を再調整する
3. `ページ.mkv`
   - 同条件へ入れないことを回帰で確認する

## 7. 回帰観点
- `35967.mp4`
  - 救済へ入る
- `インデックス破壊-093-2-4K.mp4`
  - 誤って `35967型` に入らない
- `画像1枚あり顔.mkv`
  - 短尺 fallback 強化の恩恵を受ける
- `画像1枚ありページ.mkv`
  - `顔.mkv型` と同じ扱いにしない
- near-black 2件
  - 影響なし

## 8. 関連
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Implementation Plan_workthree_35967型救済条件の本線反映_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_35967型判定基準と本線反映候補_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\連絡用doc_workthree_画像1枚あり顔_極小seek成功条件_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\設計メモ_workthree_35967型誤適用防止テスト観点_2026-03-11.md`
- `C:\Users\{username}\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\設計メモ_workthree_画像1枚あり顔_誤適用防止テスト観点_2026-03-11.md`
