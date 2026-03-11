# 調査結果: DropTool / Worker 変更点と影響整理

更新日: 2026-03-11
対象ブランチ: `future`
主対象コミット: `8a07365` (Worker用ドロップUIと並列実行導線を追加)

## 1. 今回の変更範囲（要点）
- `src/IndigoMovieManager.Thumbnail.DropTool` を新規追加。
  - `Drop.exe` は Explorer ドロップ受付と Worker 起動に責務を限定。
  - ドロップ入力は `%LOCALAPPDATA%\IndigoMovieManager_fork\drop-manifests` の manifest で受け渡し。
- `src/IndigoMovieManager.Thumbnail.Worker/Program.cs` を拡張。
  - 引数なし起動時は Worker 内蔵 UI (`DropToolWindow`) を表示。
  - `--drop-manifest` 指定時も UI モードで manifest 読み込み。
  - 既存の worker 引数（`--main-db` 等）がある場合は従来どおり実行。
- `src/IndigoMovieManager.Thumbnail.Worker/IndigoMovieManager.Thumbnail.Worker.csproj` に
  DropTool UI ソースのリンクコンパイルを追加。
- `IndigoMovieManager_fork.csproj` の `BuildThumbnailDropTool` ターゲットを更新。
  - 出力同梱時の旧ファイル名掃除を追加。
- `IndigoMovieManager_fork.sln` に DropTool プロジェクトを追加。

## 2. 現在時点の確認結果
- ワーキングツリー上で `src/IndigoMovieManager.Thumbnail.DropTool` / `src/IndigoMovieManager.Thumbnail.Worker*` に未コミット差分はなし。
  - 変更はコミット `8a07365` に取り込み済み。
- `MainWindow` / `ThumbnailWorkerProcessManager` / `ThumbnailCoordinatorWorkerProcessManager` の Worker 起動引数を確認。
  - 現状の起動線は `--role` `--main-db` `--owner` `--settings-snapshot` `--parent-pid` のみで、`--drop-manifest` は渡していない。
- 追加防御として `src/IndigoMovieManager.Thumbnail.Worker/Program.cs` にガードを追加。
  - `--role` など本番 worker 引数が含まれる場合は、`--drop-manifest` が混在しても UI モードへ分岐しない。
- 追加防御はテストで固定済み。
  - `Tests/IndigoMovieManager_fork.Tests/WorkerStartupModeResolverTests.cs`
  - 確認観点:
    - 引数なしは UI
    - `--drop-manifest` 単独は UI
    - worker 本線引数混在時は UI へ落ちない
    - 引数名の大文字小文字差を吸収
- `Drop.exe -> Worker UI` の短時間実機確認も実施済み。
  - `bin\x64\Debug\net8.0-windows\thumbnail-drop-tool\Drop.exe` を起動
  - `bin\x64\Debug\net8.0-windows\thumbnail-worker\IndigoMovieManager.Thumbnail.Worker.exe` が起動
  - `MainWindowTitle = サムネイル Worker`
  - `Drop.exe` は起動後に残留せず終了
  - 停止後に Worker 残留プロセスなし
- `--drop-manifest` と worker 本線引数の混在ケースも実プロセスで確認。
  - `--drop-manifest dummy-manifest.json --role normal --main-db dummy-main.wb --owner test-owner --settings-snapshot dummy-settings.json`
  - 結果: `Worker.exe` は UI を保持せず早期終了
  - 解釈: Drop UI への誤分岐で居座る挙動は発生していない
- ソリューションビルドは成功。
  - 実行コマンド:
    - `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe IndigoMovieManager_fork.sln /t:Build /p:Configuration=Debug /p:Platform=x64 /m /nologo`
- 対象13ファイルに対するローカル固有情報（絶対パス/ユーザー名/メール等）簡易スキャンで該当なし。

## 3. 運用上の注意点（リスク）
- `Worker.exe` は「引数なし」で UI モードへ入る。
  - 既存運用で「引数なし起動=エラー扱い」を前提にした外部監視がある場合、挙動差分になる。
- `--drop-manifest` が存在すると UI モードを優先する。
  - 誤って worker 用引数に `--drop-manifest` が混在すると、意図せず UI 起動へ分岐する。
- Worker 側が DropTool ソースをリンクコンパイルしているため、
  - DropTool 側の UI/補助クラス変更が Worker ビルドにも直接影響する。

## 4. 次アクション（推奨）
- Coordinator/本体から Worker 起動時の引数セットを再確認し、
  - `--drop-manifest` が混入しないことをテストで固定する。
- 監視・自動起動系で「引数なし Worker 起動」を使っていないか確認する。
- 将来的に責務をさらに分離する場合は、
  - Worker 内リンクコンパイルをやめ、UI 専用実体を DropTool 側へ寄せる方針を検討する。
