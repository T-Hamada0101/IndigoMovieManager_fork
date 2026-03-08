# Implementation Plan: SWF最小統合口設計（2026-03-08）

## 1. 目的
- `Thumbnail/Swf` 配下で先行実装した SWF 固有ロジックを、既存本線へ最小差分で接続する。
- 接続先は `ThumbnailCreationService` のみとし、`MainWindow` や `ThumbnailEngineRouter` へ SWF 固有判断を広げない。
- 失敗時は既存の `Flash` プレースホルダー経路へ安全に落とす。

## 2. 固定前提
- SWF固有ロジック本体は `Thumbnail/Swf` 配下から出さない。
- `ThumbnailCreationService` には「判定して委譲する入口」だけを追加する。
- `ThumbnailEngineRouter` には SWF 用の分岐を入れない。
- `MainWindow` 側は触らない。
- `ffmpeg.exe` 実行は `SwfThumbnailGenerationService` 側で閉じる。

## 3. 現状の接続候補

### 3.1 現状のSWF経路
- `ThumbnailCreationService.CreateThumbAsync` では、SWF は `IsUnsupportedPrecheck` として扱われている。
- 既存コード上の要点:
  - SWF判定:
    - `IsSwfFile`
    - `TryDetectSwfKnownSignature`
  - 非対応事前判定分岐:
    - `cacheMeta.IsUnsupportedPrecheck`
  - Flashプレースホルダー:
    - `FailurePlaceholderKind.FlashVideo`

### 3.2 最小統合口の候補位置
- 第1候補:
  - `DRM 前処理` の後
  - `unsupported precheck` の前
- 理由:
  - DRM扱いとは別の意味である
  - 現状の `unsupported precheck` をSWFから切り離しやすい
  - 既存の通常動画フローへ影響を広げにくい

## 4. 統合後の責務分担

### 4.1 ThumbnailCreationService が持つ責務
- `movieFullPath` が SWF かどうかを判定する
- SWF用の `SwfThumbnailCaptureOptions` を組み立てる
- `SwfThumbnailGenerationService` を呼ぶ
- 成功時に既存の `ThumbnailCreateResult` へ変換する
- 最終失敗時に `Flash` プレースホルダーへ落とす

### 4.2 Thumbnail/Swf 側が持つ責務
- 候補秒数の順次試行
- `ffmpeg.exe` 引数構築
- 一時JPEGの妥当性確認
- 白画面寄り判定
- 採用候補の返却

## 5. 入出力契約

### 5.1 ThumbnailCreationService -> SwfThumbnailGenerationService
渡すもの:
- `swfInputPath`
- `outputPath`
- `SwfThumbnailCaptureOptions`
- `CancellationToken`

補足:
- `outputPath` は最終保存先そのものを渡してよい
- 一時ファイル管理は `SwfThumbnailGenerationService` 側で閉じる

### 5.2 SwfThumbnailGenerationService -> ThumbnailCreationService
返すもの:
- `SwfThumbnailCandidate`

見る値:
- `IsFrameAccepted`
- `IsProcessSucceeded`
- `RequestedCaptureSec`
- `FailureReason`
- `FfmpegError`
- `OutputPath`

### 5.3 ThumbnailCreationService の返却
成功時:
- `CreateSuccessResult(saveThumbFileName, durationSec, previewFrame)`
失敗縮退時:
- `TryCreateFailurePlaceholderThumbnail(..., FailurePlaceholderKind.FlashVideo, ...)`
プレースホルダーも失敗した場合:
- `CreateFailedResult(saveThumbFileName, durationSec, error)`

## 6. 最小統合フロー
1. `CreateThumbAsync` 内で `IsSwfFile(movieFullPath)` を確認する
2. SWFなら `unsupported precheck` 分岐へ入る前に `HandleSwfThumbnailAsync` へ委譲する
3. `HandleSwfThumbnailAsync` は `SwfThumbnailCaptureOptions` を作る
4. `SwfThumbnailGenerationService.TryCaptureRepresentativeFrameAsync` を呼ぶ
5. 採用成功なら:
   - 必要なら代表1枚から既存タイル形式へ整形する
   - `ThumbnailCreateResult` へ変換して返す
6. 採用失敗なら:
   - `Flash` プレースホルダーを試す
   - だめなら通常失敗として返す

## 7. ThumbnailCreationService に追加する最小単位

### 7.1 新規 private helper
- `HandleSwfThumbnailAsync(...)`
  - SWF専用の統合口

役割:
- options 作成
- service 呼び出し
- preview 生成
- 成功/縮退の `ThumbnailCreateResult` 化

### 7.2 新規 private helper
- `CreateSwfCaptureOptions(TabInfo tbi, bool isResizeThumb)`
  - 幅・高さ・候補秒数・タイムアウトを確定する

### 7.3 新規 private helper
- `CreateSwfResultFromRepresentativeFrame(...)`
  - 代表1枚を既存サムネ保存形式へ載せる

補足:
- この helper の中でだけ、`ThumbInfo` と preview の既存互換を吸収する

## 8. unsupported precheck からの切り離し方

### 8.1 推奨
- `CachedMovieMeta` に `IsSwfCandidate` と `SwfDetail` を追加する

理由:
- SWFだけを `unsupported precheck` から抜ける
- 他の非対応入力との意味を混ぜずに済む
- 既存コードの影響範囲が読みやすい

### 8.2 非推奨
- `IsUnsupportedPrecheck` のまま SWF と他入力を混在させて if を増やす

理由:
- 分岐の意味が崩れる
- 将来またFlashだけ特別扱いになって読みにくくなる

## 9. 成功時の既存保存形式への橋渡し

### 9.1 最小実装
- 代表1枚のJPEGを最終サムネイルとして使う
- 必要なら既存レイアウト互換のため、同一画像を複製してタイル化する
- `ThumbInfo` の秒数配列は `RequestedCaptureSec` を全コマへ入れる

### 9.2 プレビュー
- 最終画像の先頭フレーム相当から `ThumbnailPreviewFrame` を作る
- 既存の preview DTO をそのまま使う

## 10. 失敗時の落とし方

### 10.1 第1段階
- `SwfThumbnailGenerationService` の候補試行が全滅

### 10.2 第2段階
- `FailurePlaceholderKind.FlashVideo` でプレースホルダー作成

### 10.3 第3段階
- プレースホルダーも失敗した場合だけ `CreateFailedResult`

## 11. ログ契約
- `ThumbnailCreationService` 側で残すべきログ:
  - SWF統合口へ入ったこと
  - 採用秒数
  - 最終失敗理由
  - Flashプレースホルダーへ落ちたこと
- `Thumbnail/Swf` 側で残すべきログ:
  - 各候補の reject / accept
  - unreadable image
  - blank / loading screen reject
  - ffmpeg stderr 要約

## 12. 変更対象ファイル

### 12.1 既存本線で触るファイル
1. `Thumbnail/ThumbnailCreationService.cs`

### 12.2 既存本線で原則触らないファイル
1. `Thumbnail/Engines/ThumbnailEngineRouter.cs`
2. `Thumbnail/MainWindow.ThumbnailCreation.cs`
3. `MainWindow.xaml.cs`

### 12.3 SWF側で必要なら拡張するファイル
1. `Thumbnail/Swf/SwfThumbnailGenerationService.cs`
2. `Thumbnail/Swf/SwfThumbnailCaptureOptions.cs`
3. `Thumbnail/Swf/SwfThumbnailCandidate.cs`

## 13. 受け入れ条件
- [ ] 本線への接続変更が実質 `ThumbnailCreationService` に閉じている
- [ ] SWF固有ロジック本体が `Thumbnail/Swf` 配下に残っている
- [ ] SWFだけ `unsupported precheck` から分離されている
- [ ] 最終失敗時は既存 `Flash` プレースホルダーへ落とせる
- [ ] 通常動画のルーター・MainWindow・Queue 処理に影響を広げていない

## 14. 次の実装順
1. `CachedMovieMeta` の SWF 専用フラグを設計どおり追加する
2. `ThumbnailCreationService` に `HandleSwfThumbnailAsync` を追加する
3. SWF判定分岐を `unsupported precheck` の手前へ差し込む
4. Flashプレースホルダーへの縮退を接続する
5. 既存テストの SWF 前提を差し替える
