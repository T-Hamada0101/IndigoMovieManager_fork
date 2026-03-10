# サムネイルWorker実機確認チェックリスト 2026-03-08

## 1. 目的
- build と test が通った現行コードを、実機操作で最終確認する。
- `normal / idle` Worker 分離、別窓 Viewer、下部 `サムネイル` タブ要約が期待どおり動くかを見る。

## 2. 前提
- 実行対象
  - `<fork-repo-root>/bin/x64/Debug/net8.0-windows/IndigoMovieManager_fork.exe`
- ログ
  - `%LOCALAPPDATA%/IndigoMovieManager_fork/logs/debug-runtime.log`
  - `%LOCALAPPDATA%/IndigoMovieManager_fork/logs/thumbnail-worker-*.log`
- 進捗Viewer
  - `<fork-repo-root>/bin/x64/Debug/net8.0-windows/thumbnail-progress-viewer/IndigoMovieManager.Thumbnail.ProgressViewer.exe`

## 3. 起動確認
### 3.1 本体起動
- 手順
  - 本体を起動する。
  - 任意の `.wb` を開く。
- 期待値
  - 起動で例外ダイアログが出ない。
  - DB オープン後に画面が応答する。

### 3.2 Worker supervisor 起動
- 手順
  - `debug-runtime.log` を確認する。
- 期待値
  - `worker mode enabled`
  - `worker started: role=Normal`
  - `worker started: role=Idle`
  が出る。

### 3.3 Viewer 起動
- 手順
  - 下部 `サムネイル` タブを開く。
  - 別窓 Viewer が自動起動するか確認する。
- 期待値
  - 下部タブ 1 行目に `別窓ビューアー稼働中` が出る。
  - 別窓 Viewer が表示される。

## 4. サムネイル作成確認
### 4.1 通常動画
- 手順
  - 未作成サムネイルを含む DB を開く。
  - 作成開始を待つ。
- 期待値
  - `thumbnail-create-process.csv` に `success` が増える。
  - `normal` Worker log に `consumer lease` と `autogen` 実行が出る。
  - サムネイル画像が `Thumb\120x90x3x1` 配下へ生成される。

### 4.2 巨大動画 / 再試行
- 手順
  - 巨大動画か再試行対象を含む状態で作成を回す。
- 期待値
  - `idle` Worker log に処理記録が出る。
  - `ffmpeg.exe` は `idle` 系で動く。
  - 通常動画の処理が完全停止しない。

### 4.3 失敗反映
- 手順
  - 意図的に壊れた入力を含む DB で作成を回す。
- 期待値
  - progress の `failed` が増える。
  - 下部タブの progress 行だけ赤強調になる。
  - 必要なら `#ERROR.jpg` が生成される。

## 5. UI確認
### 5.1 下部 `サムネイル` タブ
- 期待値
  - 3 行で表示される。
    - viewer状態
    - `進捗: 稼働 / 待機 / 失敗 / 完了`
    - `Worker: 通常 / ゆっくり`
  - 異常時は該当行だけ強調される。

### 5.2 別窓 Viewer
- 期待値
  - progress と health が更新される。
  - 本体を閉じても本体巻き込みで固まらない。

## 6. 優先度確認
- 手順
  - タスクマネージャーまたは Process Explorer で優先度を見る。
- 期待値
  - 本体 UI は `Normal`
  - `normal` Worker は `BelowNormal`
  - `idle` Worker は `Idle`

## 7. DB切替確認
- 手順
  - 別の `.wb` へ切り替える。
- 期待値
  - 旧 DB 向け Worker が残り続けない。
  - 新 DB で Worker / Viewer が再接続される。
  - health / progress は新 DB の owner に切り替わる。

## 8. 異常時の一次切り分け
- `Worker未配置`
  - 下部タブ worker 行で `未配置`
  - `thumbnail-worker` 配下の同梱を確認
- `起動失敗`
  - 下部タブ worker 行で `起動失敗`
  - `debug-runtime.log` の `worker exited` を確認
- `DLL不足`
  - 下部タブ worker 行で `DLL不足`
  - `thumbnail-worker\runtimes\win-x64\native\e_sqlite3.dll` を確認
- `DB不一致`
  - 下部タブ worker 行で `DB不一致`
  - 現在 DB と health snapshot の `MainDbFullPath` を確認

## 9. 合格条件
- 本体起動からサムネイル作成まで止まらず流れる。
- `normal / idle` Worker が分離して動く。
- 下部 `サムネイル` タブが 3 行要約で更新される。
- 別窓 Viewer が更新される。
- DB切替で旧 Worker が残らない。
- 実機上で致命停止や UI フリーズが出ない。
