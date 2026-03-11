# 引き継ぎdoc: ラ・ラ・ランド 2_2 idle/recovery 無言停止追跡 2026-03-11

## 1. この引き継ぎdocの目的
- `Thumbnail\019ccded-faa1-7813-9315-dddfd8490b51.txt` の会話ログを、次の担当者がそのまま追える形へ短文化する。
- 主題は `本線` の `HangSuspected` / `lease未着手回収` 実装の継続確認と、`ラ・ラ・ランド 2_2` の `idle/recovery` 無言停止の切り分けである。

## 2. 前提整理
- 途中で `workthree` へ誤って寄ったが、取り消し済み。
- 継続対象は本線:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork`
- 直近の本線課題は、`QueueDb` の `Processing` 残留と `HangSuspected` 実機確認。

## 3. このスレッドで本線へ入った内容
- `src\IndigoMovieManager.Thumbnail.Queue\QueueDb\QueueDbService.cs`
  - `StartedAtUtc=''` の未着手 lease を 20 秒 grace 超過で `Pending` へ戻す fix を追加済み。
  - read 系からも stale 回収が走るようにし、UI / coordinator の読取だけでも残留を回収できる形にした。
- `src\IndigoMovieManager.Thumbnail.Queue\ThumbnailQueueProcessor.cs`
  - `Running` 停滞 watchdog を追加済み。
  - `await` 前同期ブロック対策として `Task.Factory.StartNew(...).Unwrap()` を導入済み。
  - さらに `idle/recovery` 無言停止切り分け用に観測ログを追加済み。
- `Tests\IndigoMovieManager_fork.Tests\QueueDbDemandSnapshotTests.cs`
  - `_steph` 型の「開始されない lease が recovery 需要として残り続ける」ケースを固定するテストを追加済み。
- `Thumbnail\Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md`
  - `16. Running停滞 watchdog メモ`
  - `17. lease未着手残留 回収メモ`
  - `18. idle/recovery 無言停止 観測ログメモ`
  を反映済み。

## 4. 実機確認で確定したこと

### 4.1 `_steph__094110-vid1`
- これは最終的に通っている。
- 一時的に `Object reference not set to an instance of an object.` と `#ERROR.jpg` 作成は出るが、その後 `ffmpeg1pass` で `thumbnail done` まで到達した。
- QueueDB 最終状態も `Done`。
- したがって、今の主停止点は `_steph` ではない。

### 4.2 `「ラ・ラ・ランド」は少女漫画か！？ 2_2`
- normal 側は `autogen` の `No frames decoded` を経て、`ffmpeg1pass` fallback 後に `retry-scheduled` まで正常に進んでいる。
- その直後、idle/recovery 側で
  - `consumer lease: acquired=1 role=Idle`
  - `engine selected: id=autogen`
  までは出る。
- しかし、その後の
  - `thumbnail create failed`
  - `consumer timeout`
  - `consumer failed`
  が出ずに止まる。
- 18:32 台に同じ job をもう一度取り直しており、再度 `engine selected: id=autogen` の後で無言停止した。
- QueueDB では `QueueId=919` が `Status=Processing` のまま残留し、`OwnerInstanceId=thumb-idle:...`、`StartedAtUtc=2026-03-11T09:27:14.710Z` まで確認された。
- coordinator snapshot では `RunningRecoveryCount=1`、`HangSuspectedCount=0`。

## 5. ここまでの判断
- 今の本命課題は `_steph` ではなく、`ラ・ラ・ランド 2_2` の `idle/recovery` 実行である。
- normal 側の失敗処理は筋が通っている。
- 穴は `HangSuspected` の分類そのものではなく、`idle/recovery` 側の無言停止を watchdog 対象へ載せ切れていない点にある。
- 停止位置の候補は次の 2 つまで狭まっている。
  - `ProcessLeasedItemAsync` 入口から `processingAction` 開始直後まで
  - worker 再起動で同一 job を取り直す経路

## 6. 追加済み観測ログ
- `consumer dispatch begin`
- `consumer lane entered`
- `consumer running marked`
- `consumer processing invoke`
- `consumer processing watchdog start`
- `consumer processing action begin`
- `consumer processing action returned`
- `consumer processing action completed`
- `consumer processing action canceled`
- `consumer processing action faulted`

このログで見たいのは次の切り分け。
- `ProcessLeasedItemAsync` 入口までは来ているか
- `processingAction` が `Task` を返す前で止まるか
- `Task` は返るが完了しないか

## 7. ログ上の補足
- `debug-runtime.log` では `missing-thumb rescue` と `db_skipped_processing=1` が数秒おきに出続けている。
- これは watcher 側の未サムネ救済が `Processing` 行に弾かれている結果であり、主因ではない。
- ただし観測ノイズにはなるため、切り分け時は worker ログと QueueDB を優先して見る。

## 8. この txt が止まっている位置
- 最後の依頼は `ラ・ラ・ランド 2_2 をもう一度流した` 後の追跡。
- その時点で次にやる予定だった作業は以下。
  1. 最新の normal / idle worker ログ実名を取り直す
  2. `ラ・ラ・ランド 2_2` と `consumer processing ...` の並びだけ再抽出する
  3. QueueDB の現行行を見て、`Pending` に戻ったのか `Processing` 残留なのかを確定する
- つまり、観測ログ追加後の再実行結果は、まだこの txt には確定結果として残っていない。

## 9. 次担当の最短アクション
1. 最新の `thumbnail-worker-thumb-normal_*.log` と `thumbnail-worker-thumb-idle_*.log` を特定する。
2. `ラ・ラ・ランド 2_2` の直前直後で、`consumer processing ...` のどこまで出たかを確認する。
3. 同時刻の QueueDB 行を見て、`StartedAtUtc`、`LeaseUntilUtc`、`OwnerInstanceId`、`Status` を確定する。
4. `consumer processing action begin` 前で止まるなら、`ProcessLeasedItemAsync` 入口から `processingAction` 呼び出しまでを重点確認する。
5. `consumer processing action returned` まで出て完了しないなら、watchdog が監視している `Task` の掴み方を見直す。

## 10. 関連ファイル
- 元ログ:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\019ccded-faa1-7813-9315-dddfd8490b51.txt`
- 実装本体:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\src\IndigoMovieManager.Thumbnail.Queue\QueueDb\QueueDbService.cs`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\src\IndigoMovieManager.Thumbnail.Queue\ThumbnailQueueProcessor.cs`
- テスト:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Tests\IndigoMovieManager_fork.Tests\QueueDbDemandSnapshotTests.cs`
- 計画書:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\Implementation Plan_Queue実行状態分離とHangSuspected_実装計画兼タスクリスト_2026-03-10.md`

## 11. 一言要約
- 本線の主課題は `_steph` ではなく、`ラ・ラ・ランド 2_2` の `idle/recovery` 無言停止である。
- `HangSuspected` の器はできているが、無言停止が watchdog 監視へ乗り切っていない可能性が高い。
