# 709a137 実測手順

## 目的

- 初期原型 `709a137` を `D:\BentchItem_HDD` で再現可能な形で測る。
- 上流現行と同じ入力を使い、走査開始からサムネ作成完了までの流れを比較できる状態にそろえる。

## 前提

- 対象実行ファイル  
  `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\bench_worktrees\709a137-baseline\bin\x64\Debug\net8.0-windows\IndigoMovieManager.exe`
- 入力フォルダ  
  `D:\BentchItem_HDD`
- ベンチDB  
  `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\bench\709a137_hdd_bench.wb`
- サムネ出力  
  `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\bench_output\709a137_hdd\Thumb`
- ブックマーク出力  
  `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\bench_output\709a137_hdd\Bookmark`
- ログ  
  `C:\Users\na6ce\AppData\Local\IndigoMovieManager_bench_709a137\logs\bench-runtime.log`

## 手順

1. ベンチDBと既存成果物を掃除して再作成する。

```powershell
pwsh -File "C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\Test\prepare_709a137_bench_db.ps1" -Recreate -ResetArtifacts
```

2. 環境変数でベンチDBを差し込み、709a137版を起動する。

```powershell
Start-Process `
    -FilePath "C:\Users\na6ce\source\repos\IndigoMovieManager_fork\bench_worktrees\709a137-baseline\bin\x64\Debug\net8.0-windows\IndigoMovieManager.exe" `
    -WorkingDirectory "C:\Users\na6ce\source\repos\IndigoMovieManager_fork\bench_worktrees\709a137-baseline\bin\x64\Debug\net8.0-windows" `
    -Environment @{ IMM_BENCH_DB_PATH = "C:\Users\na6ce\source\repos\IndigoMovieManager_fork\bench\709a137_hdd_bench.wb" }
```

3. ログ末尾を監視し、`scan_end` と `thumb_end` の進み方を確認する。

```powershell
Get-Content -Path "C:\Users\na6ce\AppData\Local\IndigoMovieManager_bench_709a137\logs\bench-runtime.log" -Encoding UTF8 -Wait
```

## 観測ポイント

- `db_open_end` が出るか
- `scan_start` から `scan_end` まで到達するか
- `thumb_start` と `thumb_end` が同数まで進むか
- 出力サムネ枚数が `Thumb` フォルダに増えるか

## 補足

- 709a137 は起動時の自動オープンが設定依存なので、ベンチ時だけ `IMM_BENCH_DB_PATH` を最優先で読むようにしてある。
- DBスキーマ生成は現行版の `CreateDatabase` を流用し、その上に旧版が使う `watch/system` 値だけ上書きしている。
- 比較対象はまず `D:\BentchItem_HDD` を先に回し、その後 `D:\BentchItem_EXBIG` を別枠で見る。