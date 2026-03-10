# 現行フォーク 実機ベンチ手順 AI向け 2026-03-08

## 目的

- 現行フォーク本体を、`初期原型709a137ベンチ結果_HDD_2026-03-08.md` と同系統の「実機ベンチ」として再実行する。
- サムネイルエンジン比較ベンチとは分ける。
- 次回の AI は、この手順をそのまま再現すればよい。

## 前提

- 対象アプリ  
  `<fork-repo-root>/bin/x64/Debug/net8.0-windows/IndigoMovieManager_fork.exe`
- ソリューション  
  `<fork-repo-root>/IndigoMovieManager_fork.sln`
- ビルドコマンド  
  `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`
- PowerShell は 7.x を使う。
- 入力フォルダは通常これを使う。  
  `D:\BentchItem_HDD`
- この実機ベンチは、GUI本体を起動して DB を開き、走査とサムネ生成を観測する方式。

## 保存先の標準

- ベンチDB  
  `<fork-repo-root>/bench/current_fork_hdd_bench.wb`
- サムネ出力  
  `<fork-repo-root>/bench_output/current_fork_hdd/Thumb`
- ブックマーク出力  
  `<fork-repo-root>/bench_output/current_fork_hdd/Bookmark`
- 本体ログ  
  `%LOCALAPPDATA%/IndigoMovieManager_fork/logs/debug-runtime.log`
- Workerログ  
  `%LOCALAPPDATA%/IndigoMovieManager_fork/logs/thumbnail-worker-*.log`

## このベンチの位置づけ

- これは `run_thumbnail_engine_bench.ps1` 系の「エンジン比較」ではない。
- これは GUI本体の `OpenDatafile`、`CheckFolderAsync`、Worker処理、出力jpg件数を観測する「実機ベンチ」。
- 結果保存先の基準ドキュメントはこれ。  
  `Docs/現行フォーク実機ベンチ結果_HDD_2026-03-08.md`

## 実行前の注意

- 実行中の `IndigoMovieManager_fork`、`IndigoMovieManager.Thumbnail.Worker`、`IndigoMovieManager.Thumbnail.ProgressViewer` は止める。
- 通常運用へ戻した後は、`MainWindow.xaml.cs` に `IMM_BENCH_DB_PATH` の強制オープン差分は残さない。
- つまり、次回ベンチ時は「一時的に差し込んで、終わったら戻す」が原則。

## 実施フロー

### 1. 実行中プロセスを止める

```powershell
pwsh -NoLogo -NoProfile -Command "Get-Process IndigoMovieManager_fork,IndigoMovieManager.Thumbnail.Worker,IndigoMovieManager.Thumbnail.ProgressViewer -ErrorAction SilentlyContinue | Stop-Process -Force"
```

### 2. ベンチ用の最小差分を一時適用する

- 対象  
  `MainWindow.xaml.cs`
- 差し込み位置  
  `MainWindow_ContentRendered`
- 目的  
  環境変数 `IMM_BENCH_DB_PATH` がある時だけ、その DB を `AutoOpen` より優先して開く。

差し込み内容の要点はこれ。

```csharp
string benchDbPath = Environment.GetEnvironmentVariable("IMM_BENCH_DB_PATH") ?? "";
if (!string.IsNullOrWhiteSpace(benchDbPath) && Path.Exists(benchDbPath))
{
    DebugRuntimeLog.Write("bench", $"db override: '{benchDbPath}'");
    OpenDatafile(benchDbPath);
}
else if (Properties.Settings.Default.AutoOpen)
{
    ...
}
```

- ベンチ終了後は、この差分を必ず戻す。

## 3. ビルドする

```powershell
pwsh -NoLogo -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' '<fork-repo-root>\\IndigoMovieManager_fork.sln' /t:Build /p:Configuration=Debug /p:Platform=x64 /m"
```

## 4. DB準備用の一時DLLを置く

- 既存の `prepare_upstream_current_bench_db.ps1` は、反射で `IndigoMovieManager.DB.SQLite.CreateDatabase` を呼ぶ。
- 現行フォーク実機ベンチでは、そのままだと DLL 名が合わないため、一時的に `IndigoMovieManager_fork.dll` を `IndigoMovieManager.dll` 名で同じフォルダへ置く。
- これは DB 準備専用の暫定対応であり、ベンチ後は削除する。

```powershell
pwsh -NoLogo -NoProfile -Command "Copy-Item '<fork-repo-root>\\bin\\x64\\Debug\\net8.0-windows\\IndigoMovieManager_fork.dll' '<fork-repo-root>\\bin\\x64\\Debug\\net8.0-windows\\IndigoMovieManager.dll' -Force"
```

## 5. ベンチDBと出力先をクリーン作成する

```powershell
pwsh -NoLogo -NoProfile -File "<fork-repo-root>\\Thumbnail\\Test\\prepare_upstream_current_bench_db.ps1" -UpstreamBuildDir "<fork-repo-root>\\bin\\x64\\Debug\\net8.0-windows" -DbPath "<fork-repo-root>\\bench\\current_fork_hdd_bench.wb" -InputFolder "<input-video-root>" -ThumbFolder "<fork-repo-root>\\bench_output\\current_fork_hdd\\Thumb" -BookmarkFolder "<fork-repo-root>\\bench_output\\current_fork_hdd\\Bookmark" -Recreate -ResetArtifacts
```

## 6. 環境変数付きで本体を起動する

```powershell
pwsh -NoLogo -NoProfile -Command "Start-Process -FilePath '<fork-repo-root>\\bin\\x64\\Debug\\net8.0-windows\\IndigoMovieManager_fork.exe' -WorkingDirectory '<fork-repo-root>\\bin\\x64\\Debug\\net8.0-windows' -Environment @{ IMM_BENCH_DB_PATH = '<fork-repo-root>\\bench\\current_fork_hdd_bench.wb' }"
```

- 起動後は `db override` が本体ログへ出ることを確認する。

## 7. 観測ポイント

- 本体ログで見る。  
  `%LOCALAPPDATA%/IndigoMovieManager_fork/logs/debug-runtime.log`
- まず確認する行
  - `db override`
  - `OpenDatafile`
  - `CheckFolderAsync`
  - `watch-check scan end`
- Workerログで見る。  
  `%LOCALAPPDATA%/IndigoMovieManager_fork/logs/thumbnail-worker-*.log`
- 追加で数えるもの
  - `movie` テーブル件数
  - `movie_name + hash` 一意件数
  - 出力jpg件数
  - `#ERROR.jpg` 件数
  - normal worker の `done`
  - idle worker の `done`

## 8. 終了判定

- `watch-check scan end` が出る。
- 出力jpg件数の増加が止まる。
- Workerログの進行が収束する。
- watch poll は残り続ける場合があるので、完全無活動を待ちすぎない。
- 結果固定の時点でプロセスを停止し、件数を記録する。

## 9. ベンチ後の後始末

### 9-1. プロセス停止

```powershell
pwsh -NoLogo -NoProfile -Command "Get-Process IndigoMovieManager_fork,IndigoMovieManager.Thumbnail.Worker,IndigoMovieManager.Thumbnail.ProgressViewer -ErrorAction SilentlyContinue | Stop-Process -Force"
```

### 9-2. 一時DLL削除

```powershell
pwsh -NoLogo -NoProfile -Command "if (Test-Path '<fork-repo-root>\\bin\\x64\\Debug\\net8.0-windows\\IndigoMovieManager.dll') { Remove-Item '<fork-repo-root>\\bin\\x64\\Debug\\net8.0-windows\\IndigoMovieManager.dll' -Force }"
```

### 9-3. `MainWindow.xaml.cs` を通常状態へ戻す

- `IMM_BENCH_DB_PATH` の一時差分を消す。
- `AutoOpen` の通常分岐へ戻す。

### 9-4. ビルドして通常状態を確認する

```powershell
pwsh -NoLogo -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' '<fork-repo-root>\\IndigoMovieManager_fork.sln' /t:Build /p:Configuration=Debug /p:Platform=x64 /m"
```

### 9-5. 本体が通常優先度で起動することを確認する

- ベンチ後の通常確認では、本体は `PriorityClass=Normal` で起動するのが確認済み。
- `Idle` は本体ではなく、サムネイル専用 `Idle worker` の役割名である。

## 結果記録のひな形

- 実行対象
- 入力フォルダ
- ベンチDB
- サムネ出力
- ログ
- 実施前に入れた最小修正
- 観測結果
- 判定
- エラー出力
- メモ

## 今回の確定結果

- 保存済みの結果  
  `Docs/現行フォーク実機ベンチ結果_HDD_2026-03-08.md`
- 同日に保存済みのエンジン比較結果  
  `Docs/現行フォークベンチ結果_HDD_2026-03-08.md`

## 次回AIへの指示

- まずこの手順書を読む。
- エンジン比較ベンチと混同しない。
- 実機ベンチでは、一時差分の追加と後始末をセットで行う。
- 結果は日付付きの新規 md へ保存し、前回結果は上書きしない。
