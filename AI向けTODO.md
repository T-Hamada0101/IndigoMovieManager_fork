# AI向けTODO

## 運用ルール
- 処理を始める前に、対象コード、関連ログ、既存ドキュメントの現状を確認する。
- 推測で進めず、確認できた事実を先に整理してから着手する。
- 変更中はこのファイルを随時更新し、完了・保留・課題を残す。
- ビルドやテストが環境要因で止まった場合は、その原因をここへ明記する。

## 現在の確認結果
- 欠損サムネ救済は、即時大量投入から DB + tab 単位のメモリ保持 + 少量排出へ変更済み。
- Watch 起動直後の自動救済は 1 回スキップ、Manual は停止しない方針になっている。
- DB 切替時に欠損救済バッファを破棄する処理は追加済み。
- DB 未登録または DB ファイル未配置の時は、監視キュー投入・全件走査・Watcher 作成を無視するガードを追加した。
- DB 未登録時は worker と関連 supervisor も起動せず、DB 接続後の起動へ回すようにした。
- Idle worker は通常ジョブ中心だと遊ぶ設計になっている。slow/recovery 需要ゼロでも slow=1 を維持し、lease も retry/大物中心で取る。
- 外部 worker モード時のアクティブ件数判定は、in-process owner ではなく normal/idle worker の Queued + Leased + Running を合算して見る必要がある。
- MainWindow / Watcher / ThumbnailCreation の現行差分は、MSBuild(Debug/x64) でビルド通過を確認した。

## TODO
- [x] 欠損救済の排出途中で quota が下がった時、未投入 batch が消えないよう修正した。
- [x] 上記の回帰テストを追加し、quota 途中低下時でも候補が失われないことを確認した。
- [x] worker 起動確認と欠損救済の共存確認を実ランタイムで再実施した。
- [x] worker 起動確認と欠損救済の共存確認で追うログ観点を整理した。
- [x] 外部 worker モードで `Pending -> Processing` へ即遷移したジョブを、UI/監視が「消えた」と誤認しないよう activeCount 判定を修正した。
- [x] MainWindow / Watcher / ThumbnailCreation の現行差分は、MSBuild(Debug/x64) でビルド確認済み。

## メモ
- 2026-03-08 時点のレビューでは、未投入 batch を `Clear()` してしまう経路が残っている可能性を確認した。
- 2026-03-11: 欠損救済は `TryPeek` で先読みし、quota 超過時は未投入 batch を先頭へ戻す形へ修正した。
- 2026-03-11: 共存確認ログは `thumbnail background ensure: mode=...` と `missing-thumb rescue: queue_mode=... active=... quota=... buffered_before=... buffered=...` を主観点にする。
- 2026-03-11: 隔離ランタイム確認では `artifacts/runtime-smoke/20260311_200403` を使い、`mode=coordinator` で起動後に `missing-thumb rescue: mode=Watch tab=0 queue_mode=coordinator active=1 quota=31 buffered_before=1 rebuilt=True prepared=1 enqueued=1 buffered=0` を確認した。`thumb-root/120x90x3x1/QGK5kJ-rQiZg5NPZ.#04e6bcdc.jpg` は 20:04:44 に再生成された。
