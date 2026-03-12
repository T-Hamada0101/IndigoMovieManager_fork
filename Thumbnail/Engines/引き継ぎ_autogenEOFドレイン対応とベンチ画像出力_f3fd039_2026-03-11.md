# 引き継ぎdoc: autogen EOFドレイン対応とベンチ画像出力 f3fd039 2026-03-11

## 1. 対象コミット
- `f3fd039`
- メッセージ: `autogenのEOFドレイン対応とベンチ画像出力追加`

## 2. このコミットの目的
- `FfmpegAutoGenThumbnailGenerationEngine` の取りこぼしを減らす。
- 超短尺動画で `No frames decoded` になる原因を切る。
- ベンチ成功時に、生成画像を動画と同じフォルダへ出して目視確認しやすくする。

## 3. 原因
- 超短尺動画では `avcodec_send_packet(...)` 自体は通るが、直後の `avcodec_receive_frame(...)` が `EAGAIN` を返したまま `av_read_frame(...)` が `EOF` になるケースがあった。
- 旧実装は `EOF` 到達後に `flush packet` を送っていなかったため、decoder 内に残っていた遅延出力フレームを回収できず、`No frames decoded` で落ちていた。

## 4. 実装内容
- `Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs`
  - `EAGAIN` 後の扱いを維持しつつ、`read_frame` が `EOF` になった後に `avcodec_send_packet(..., null)` を送り、drain する処理を追加。
  - 超短尺動画でも `autogen-seek` の詳細ログを出すように変更。
  - `read / send / receive / flush / hit / miss` の切り分けログを追加。
- `Tests/IndigoMovieManager_fork.Tests/FfmpegAutoGenThumbnailGenerationEngineTests.cs`
  - 超短尺動画で seek 詳細ログ対象になる条件のテストを追加。
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailEngineBenchTests.cs`
  - ベンチ経路でも runtime log が見えるよう、ベンチ専用 logger を追加。
- `Thumbnail/Test/run_thumbnail_engine_bench.ps1`
  - `-ExportToMovieFolder` を追加。
  - ベンチ成功時の出力画像を動画と同じフォルダへ複製できるようにした。

## 5. 確認できた改善
- 対象:
  - `画像1枚あり顔.mkv`
  - `画像1枚ありページ.mkv`
- 旧状態:
  - `receive_frame eagain` のまま `read_frame end: EOF`
  - `No frames decoded`
- 修正後:
  - `send flush packet`
  - 直後に `seek hit`
  - `autogen success`

## 6. ログ確認ポイント
- `debug-runtime.log`
  - `bench-autogen-seek`
  - `bench-autogen-shortclip-firstframe-fallback`
  - `bench-autogen-header-frame-fallback`
- 修正が効いた時の目印:
  - `send flush packet`
  - 直後の `seek hit`

## 7. ベンチ実行例
```powershell
pwsh -File .\Thumbnail\Test\run_thumbnail_engine_bench.ps1 `
  -InputMovie 'E:\_サムネイル作成困難動画\画像1枚あり顔.mkv' `
  -Engines autogen `
  -Iteration 1 `
  -Warmup 0 `
  -Priority Normal `
  -Configuration Debug `
  -Platform x64 `
  -SkipBuild `
  -ExportToMovieFolder
```

## 8. 適用時の見方
- まずは別ツリーで `git cherry-pick f3fd039` を想定。
- 適用後は上の 2 本でベンチを流し、`thumbnail-create-process.csv` が `success` になることを確認する。
- さらに `debug-runtime.log` で `send flush packet -> seek hit` が出ることを確認する。

## 9. 補足
- 今回の本線は「超短尺で packet は読めているが frame が返らない」ケースへの対応。
- `near-black` 系や長尺破損動画系の論点とは別。
