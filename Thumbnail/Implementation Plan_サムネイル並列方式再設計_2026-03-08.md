# Implementation Plan: サムネイル並列方式再設計（2026-03-08）

## 1. 目的
- `ffmpeg.exe` の長時間処理で通常系が詰まる問題を解消する。
- 初回作成と失敗再処理を分離し、巨大動画と再試行をゆっくり系へ隔離する。
- UIと設定を、新しい並列方式に合わせて整理する。

## 2. 参照
- `Thumbnail/仕様書_サムネイル並列方式再設計_2026-03-08.md`
- `Thumbnail/Flowchart_動画判定処理_失敗時処理_時系列整理_2026-03-08.md`

## 3. 実装方針

### 3.1 キュー取得
- `QueueDbService` に取得順指定を追加する。
- 通常系は未再試行ジョブをサイズ昇順で取得する。
- ゆっくり系は巨大動画と再試行ジョブを優先し、巨大動画はサイズ降順で取得する。
- 先読み件数は通常4件、ゆっくり1件、再試行1件を既定とする。

### 3.2 レーン分類
- `ThumbnailLaneClassifier` は巨大動画閾値のみで `Normal` / `Slow` を判定する。
- 小動画優先レーンは廃止し、通常系は「小さい順で拾う」実装に置き換える。

### 3.3 エンジン責務分離
- `ThumbnailEngineRouter` は `AttemptCount = 0` を `autogen`、`AttemptCount > 0` を `ffmpeg1pass` へ寄せる。
- `ThumbnailCreationService` は初回自動作成時の多段フォールバックを止め、`autogen` 単独実行に寄せる。
- 再試行時だけ `ffmpeg1pass` を主経路にし、必要な救済のみ維持する。

### 3.4 優先度
- 通常系プロセスは `BelowNormal` を維持する。
- `ffmpeg1pass` 子プロセスは `Idle` を既定にする。

### 3.5 UI整理
- `サムネイル進捗` タブを `サムネイル` へ改名する。
- CPU/GPU/HDDメーターを撤去する。
- 進捗タブのレーン説明を新構成へ合わせる。
- 共通設定から「優先レーン上限サイズ(MB)」「閾値プリセット」を外し、巨大動画判定GB閾値中心へ寄せる。

## 4. 実装反映状況（2026-03-08追記）
- 完了:
  - `QueueDbService` と `ThumbnailQueueProcessor` で、通常は小さい順、巨大動画と再試行は大きい順の取得へ切り替えた。
  - `ThumbnailEngineRouter` と `ThumbnailCreationService` で、初回は `autogen` 固定、再試行は `ffmpeg1pass` 主経路へ分離した。
  - `FfmpegOnePassThumbnailGenerationEngine` の子プロセス優先度既定を `Idle` にした。
  - `MainWindow.xaml` / `MainWindow.xaml.cs` / `CommonSettingsWindow.xaml` / `CommonSettingsWindow.xaml.cs` で、`サムネイル` タブ化、メーター撤去、設定項目整理を反映した。
  - 旧 `ThumbnailPriorityLaneMaxMb` 系 UI と保存処理を削除し、`プリセット + 巨大動画判定GB閾値` に寄せた。
  - `ThumbnailLaneClassifierTests` を追加し、環境変数による閾値上書き挙動を固定した。
- 実装上の現在値:
  - 進捗表示ラベルは `ゆっくり / 再試行専 / 通常 n`
  - レーン説明は `ゆっくり=巨大動画 / 再試行専=失敗再処理 / 通常=小さい順`
- 確認結果:
  - 全テスト `230 pass / 7 skip / 0 fail`
  - MSBuild x64 Debug `0 warning / 0 error`

## 5. 作業ステップ
1. 計画書と仕様書を追加する。
2. `QueueDbService` と `ThumbnailQueueProcessor` のキュー取得順を変更する。
3. `ThumbnailLaneClassifier` と進捗表示のレーン概念を更新する。
4. `ThumbnailEngineRouter` と `ThumbnailCreationService` の初回/再試行経路を分離する。
5. `FfmpegOnePassThumbnailGenerationEngine` の優先度既定値を `Idle` にする。
6. `MainWindow.xaml` と `CommonSettingsWindow.xaml` を新表示へ整理する。
7. テストとMSBuildで確認する。

進捗:
- 1-7 完了

## 6. 影響ファイル
- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailLaneClassifier.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailProgressRuntime.cs`
- `Thumbnail/Engines/ThumbnailEngineRouter.cs`
- `Thumbnail/ThumbnailCreationService.cs`
- `Thumbnail/Engines/FfmpegOnePassThumbnailGenerationEngine.cs`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `CommonSettingsWindow.xaml`
- `CommonSettingsWindow.xaml.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/Properties/InternalsVisibleTo.Tests.cs`
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailLaneClassifierTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/AutogenExecutionFlowTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/FfmpegOnePassThumbnailGenerationEngineTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailProgressRuntimeTests.cs`

## 7. 完了条件
- 初回自動作成で `ffmpeg1pass` が通常系へ即時混入しない。
- 再試行ジョブはゆっくり系へ入り、`ffmpeg1pass` は `Idle` 既定で動く。
- 巨大動画は巨大動画判定GB閾値でゆっくり系優先になる。
- 進捗タブ名が `サムネイル` になり、メーターが消えている。
- 共通設定で閾値プリセットが消え、巨大動画判定GB閾値だけが前面に出ている。

判定:
- 完了
