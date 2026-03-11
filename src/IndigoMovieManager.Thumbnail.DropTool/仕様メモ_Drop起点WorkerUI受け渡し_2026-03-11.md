# Drop 起点 Worker UI 受け渡しメモ

更新日: 2026-03-11

## 変更概要
- `Drop.exe` は Explorer からのドロップ受け取り専用にする
- `Worker.exe` は UI 表示とサムネイル処理を担当する
- ドロップされた入力は一時 manifest 経由で `Worker.exe` へ渡す
- `Worker.exe` の UI は中央表示し、起動直後に前面へ寄せて見失いにくくする
- `Worker.exe` の画面タイトルは `サムネイル Worker` とし、開始時に別ウィンドウを起動しないことを明示する
- 入力でフォルダを受けた時は、既定の出力先を配下の `Thumb` へ自動補完する
- 並列数は UI から指定し、ファイル単位の並列処理として実行する

## 責務分離
- `Drop.exe`
  - 起動引数のファイル/フォルダを正規化する
  - `%LOCALAPPDATA%\IndigoMovieManager_fork\drop-manifests` に manifest を作る
  - `Worker.exe --drop-manifest <manifestPath>` を起動して終了する
- `Worker.exe`
  - `--drop-manifest` を読んで初期入力へ流し込む
  - 既存の入力一覧、出力フォルダ、サイズ選択 UI を表示する
  - 開始ボタン以降のサムネイル処理を担当する

## 意図
- Explorer ドロップ受付と実処理 UI を分けて、責務を単純化する
- 大量パスをコマンドラインへ直接積まず、長さ制限を避ける
- UI と処理の本体を `Worker.exe` へ集約して、今後の設定追加先を一本化する
