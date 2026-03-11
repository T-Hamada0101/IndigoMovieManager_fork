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
- [ ] 欠損救済の排出途中で quota が下がった時、未投入 batch が消えないよう修正する。
- [ ] 上記の回帰テストを追加し、quota 途中低下時でも候補が失われないことを確認する。
- [ ] worker 起動確認と欠損救済の共存確認を再実施し、ログ観点を整理する。
- [x] 外部 worker モードで `Pending -> Processing` へ即遷移したジョブを、UI/監視が「消えた」と誤認しないよう activeCount 判定を修正した。
- [x] MainWindow / Watcher / ThumbnailCreation の現行差分は、MSBuild(Debug/x64) でビルド確認済み。

## メモ
- 2026-03-08 時点のレビューでは、未投入 batch を `Clear()` してしまう経路が残っている可能性を確認した。
