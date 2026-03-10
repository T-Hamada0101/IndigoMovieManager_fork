# 連絡用doc: AutogenRepairPlaygroundTests API追従要否 (2026-03-11)

## 結論
- `AutogenRepairPlaygroundTests` は現状そのままでは検証ハーネスとして使えない。
- 原因は `TabInfo / MovieInfo` まわりの現行 API への追従漏れ。
- これは本体側の `QH-011` 実装不具合ではなく、playground test 側の別件。
- 本体アプリ本体のビルドは通過済み。

## 共有したい事実
- `IndigoMovieManager_fork.csproj` の Debug|x64 ビルドは成功している。
- 一方で `Tests\IndigoMovieManager_fork.Tests.csproj` は、`AutogenRepairPlaygroundTests.cs` を含む既存テスト側の API 不一致で止まる。
- そのため、`AutogenRepairPlaygroundTests` を使って本体変更の成否確認をする前に、まず playground test 自体の追従修正が必要。

## 主なズレ
- `TabInfo`
  - 現在は `Width / Height / Columns / Rows` が読み取り専用。
  - 旧テスト側は古い前提の使い方が残っている可能性がある。
- `MovieInfo`
  - 現在は `ProbeMetadataSources(...)` と `DurationSec` 系の流れが主。
  - 旧テスト側は古い `Duration` / `AvgBitrate` 前提が混ざっている可能性がある。

## 別エージェントへの依頼内容
- `AutogenRepairPlaygroundTests.cs` を現行 API に追従させる。
- 目的は以下の2点に限定する。
  - playground test を再び実行可能に戻す
  - `FailureDb` 記録つきの試験ハーネスとして再利用可能にする

## 参照してほしいファイル
- [AutogenRepairPlaygroundTests.cs](c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Tests/IndigoMovieManager_fork.Tests/AutogenRepairPlaygroundTests.cs)
- [FfmpegShortClipRecoveryPlaygroundTests.cs](c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Tests/IndigoMovieManager_fork.Tests/FfmpegShortClipRecoveryPlaygroundTests.cs)
- [TabInfo.cs](c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/TabInfo.cs)
- [MovieInfo.cs](c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Models/MovieInfo.cs)
- [ThumbnailJobMaterialBuilder.cs](c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/ThumbnailJobMaterialBuilder.cs)
- [ThumbnailJobContextFactory.cs](c:/Users/na6ce/source/repos/IndigoMovieManager_fork/Thumbnail/ThumbnailJobContextFactory.cs)

## 補足
- `QH-011` で進めた `coordinator control` の `leased / running / hang` 追加は本体側へ反映済み。
- 検証が止まった理由は playground test の既存崩れであり、今回の control 追加差分そのものではない。
