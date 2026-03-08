# SWF Thumbnail Integration Plan（2026-03-08）

## 1. 目的
- SWFサムネイル対応を、既存本線へ大きく干渉させずに先行実装する。
- `Thumbnail/Swf` 配下で完結する単位を先に作り、後で最小差分で統合する。

## 2. 先行作成する責務
- `SwfThumbnailCandidate`
  - 候補秒数、採用秒数、失敗理由
- `SwfThumbnailCaptureOptions`
  - 出力サイズ、候補秒数、タイムアウト、白画面判定閾値
- `SwfThumbnailFrameAnalyzer`
  - 白画面疑い、単色寄り判定
- `SwfThumbnailFfmpegCommandBuilder`
  - `ffmpeg.exe` 引数構築
- `SwfThumbnailGenerationService`
  - 候補時刻の順次試行と採用フレーム決定

## 3. 既存本線への接続点
- 第1接続点:
  - `Thumbnail/ThumbnailCreationService.cs`
- 必要時の共通処理再利用先:
  - `Thumbnail/Engines/FfmpegOnePassThumbnailGenerationEngine.cs`

## 4. 統合順
1. `Thumbnail/Swf` 配下の型を先に作る
2. SWF用ユニットテストを別で作る
3. `ThumbnailCreationService` に最小の呼び出し口を追加する
4. 旧SWF即プレースホルダー分岐を置き換える
5. 実SWFで手動確認する

## 5. 今は避けること
- `MainWindow.xaml.cs` へのSWF固有処理追加
- `ThumbnailEngineRouter.cs` への複雑なSWF専用分岐追加
- 通常動画経路との過度な共通化

## 6. 完了条件
- SWF固有ロジックの大半が `Thumbnail/Swf` 配下にある
- 既存本線への変更が「接続口追加」と「旧分岐置換」にほぼ限られている
- 別作業との競合を最小化できている
