# 調査結果_サムネイルProcessing残留とlease先取り過多_2026-03-10

## 目的

サムネイル worker が起動中にもかかわらず、QueueDB にジョブが残り続け、処理が進まないように見える事象を整理する。

対象DB:

- `<main-db-path>`

## 結論

今回の主因は、単純な worker 全体停止ではなく、`Processing` の先取り lease が多すぎることだった。

- `worker` / `coordinator` プロセス自体は生存していた
- `ffmpeg.exe` はぶら下がっていなかった
- QueueDB では `Pending=0`、`Processing=13`、`Done=2` だった
- `Processing` 行のうち複数は heartbeat で `LeaseUntilUtc` / `UpdatedAtUtc` が進まず、実行中というより「先に lease された未着手ジョブ」に近い挙動だった

つまり、難動画で処理時間が長い状況下で、`実行枠以上の lease 先取り` が `Processing` 残留を膨らませていた。

## 観測結果

### 1. プロセス生存

実機で確認したプロセス構成は以下。

- `IndigoMovieManager_fork.exe`
- `IndigoMovieManager.Thumbnail.Coordinator.exe`
- `IndigoMovieManager.Thumbnail.Worker.exe` x 2

親子関係:

- `本体 -> Coordinator -> normal worker / idle worker`

### 2. 外部エンジン状況

- 追跡時点で `ffmpeg.exe` は存在しなかった
- よって「外部 ffmpeg 子プロセスがぶら下がって返ってこない」形ではなかった

### 3. QueueDB 状態

70秒追跡中も以下が継続した。

- `Status=Processing` が 13 件
- `Status=Done` が 2 件
- `Status=Pending` は 0 件

owner 内訳:

- `thumb-normal...` が 5 件
- `thumb-idle...` が 8 件

### 4. heartbeat とのズレ

`thumbnail-health-*.json` は更新され続けていたため worker 自体は生存していた。

一方で、古い `Processing` 行は以下が進まなかった。

- `LeaseUntilUtc`
- `UpdatedAtUtc`

このため、少なくとも一部行は「現在実行中」ではなく、「先取り lease 後に未着手のまま残っている」と判断できる。

## 根本原因の整理

### コード上の構造

`Processing` は lease 取得時点で付与される。

- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
  - `GetPendingAndLease(...)` 内で `Status = Processing`

その後、成功・失敗・キャンセルで `UpdateStatus(...)` される。

- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
  - `UpdateStatus(...)`

再投入側は `Processing` 行を上書きしない。

- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
  - `Upsert(...)`
  - `WHERE ThumbnailQueue.Status <> @Processing`

### 問題点

従来は `LeaseBatchSize` が「現在の実行枠」より大きくなり得た。

- normal worker:
  - `Math.Max(4, resolvedParallelism)`
- runtime:
  - 先読みバッファ込みで `ResolveLeaseBatchSize(...)`

このため、難動画で処理完了が遅い時に以下が起こる。

1. 実際にはすぐ着手できないジョブまで `Processing` 化
2. watcher 再投入時は `db_skipped_processing` で保護
3. ユーザー視点では「キューにあるのに動かない」

## 今回の対策

`lease` の上限を「今すぐ実行できる枠数」までに抑えた。

### 変更内容

- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailWorkerSettingsResolver.cs`
  - normal worker の `LeaseBatchSize`
  - 変更前: `Math.Max(4, resolvedParallelism)`
  - 変更後: `Math.Max(1, resolvedParallelism)`

- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
  - `ResolveLeaseBatchSize(...)`
  - 変更前: 先読みバッファを含む件数まで lease
  - 変更後: `currentParallelism` を上限に制限

## 追跡に使ったスクリプト

- `scripts/trace_thumbnail_process_tree.ps1`
  - プロセスツリー、CPU増分、child 数を短時間で確認

- `scripts/trace_thumbnail_runtime.py`
  - process / health / control / QueueDB を同時に時系列採取

出力ログ例:

- `%LOCALAPPDATA%/IndigoMovieManager_fork/logs/thumbnail-process-trace-*.log`
- `%LOCALAPPDATA%/IndigoMovieManager_fork/logs/thumbnail-runtime-trace-*.log`

## 2026-03-10 追記: owner dead の即時回収

先取り lease 抑制とは別に、`Processing` 残留のもう一つの経路として、

1. `Status=Processing` へ遷移
2. worker が停止キャンセル・異常終了
3. `Pending` / `Failed` へ戻す前に owner が死ぬ

という orphan 化がある。

この経路に対して、`QueueDbService.GetPendingAndLease(...)` の直前で

- `LeaseUntilUtc` 期限切れ
- `thumbnail-health-*.json` が `stopped / exited / start-failed / missing`
- `thumbnail-health-*.json` の heartbeat が一定時間更新されていない

のいずれかを stale とみなし、`Processing -> Pending` へ戻すよう修正した。

これにより、lease 期限 5分を待たず、死んだ worker が握っていたジョブを次ループで再取得できる。

## まだ残っている課題

今回の修正は

- `Processing` 膨張の抑制
- dead owner の即時回収

までであり、難動画そのものの停滞検知は未対応。

残課題:

- `Leased` と `Running` の状態分離
- 1ジョブの最大実行時間制限
- heartbeat は生きているが実処理が進んでいないケースの検知
- `FailureKind.HangSuspected` の導入

## 次の推奨手順

1. 実機で `lease` 抑制後の QueueDB 状態を再観測する
2. 難動画専用で `Running` 移行時刻を記録する
3. 一定時間 `Running` が進まない場合は `Pending` 戻しまたは `HangSuspected` へ遷移させる
