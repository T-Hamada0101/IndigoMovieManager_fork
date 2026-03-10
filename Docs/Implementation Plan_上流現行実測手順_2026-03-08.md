# 上流現行ベンチ実測手順 (2026-03-08)

## 結論
- 上流現行は計測ログ追加済み、ビルド成功済み。
- 実測は `D:\BentchItem_HDD` を共通入力として行う。
- ベンチログは `%LOCALAPPDATA%\IndigoMovieManager_bench_upstream_current\logs\bench-runtime.log` に出る。

## 追加した計測点
- `app_start`
- `open_datafile_start` / `open_datafile_end`
- `scan_start` / `scan_end`
- `thumb_worker_start`
- `thumb_start` / `thumb_end`
- `hash_start` / `hash_end`

## 事前準備
1. 上流現行をビルドする。
2. `Thumbnail\Test\prepare_upstream_current_bench_db.ps1` を実行して、ベンチ用 `.wb` と出力先を用意する。

```powershell
pwsh -File "<fork-repo-root>\\Thumbnail\\Test\\prepare_upstream_current_bench_db.ps1" -Recreate -ResetArtifacts
```

既定値は以下。
- DB: `<upstream-repo-root>/bench/upstream_current_bench.wb`
- 入力: `D:\BentchItem_HDD`
- サムネ出力: `<upstream-repo-root>/bench_output/upstream_current_bench/Thumb`
- ブックマーク出力: `<upstream-repo-root>/bench_output/upstream_current_bench/Bookmark`

## 実行手順
1. 上流EXEを起動する。

```powershell
Start-Process -FilePath "<upstream-repo-root>\\bin\\x64\\Debug\\net8.0-windows\\IndigoMovieManager.exe" -WorkingDirectory "<upstream-repo-root>\\bin\\x64\\Debug\\net8.0-windows"
```

2. アプリで `開く` を押し、以下を開く。
- `<upstream-repo-root>/bench/upstream_current_bench.wb`

3. 初回走査が終わるまで待つ。
- `open_datafile_end` が DB読込の完了
- `scan_end` が初回走査の完了
- `thumb_end` が各サムネ作成完了

## 最初に取るべき測定
### 1件サムネ確認
- `D:\BentchItem_HDD` の中から動画1本だけ入った小フォルダで実施する。
- まずログに `hash_*` と `thumb_*` が1件分出ることを確認する。

### 100件初回走査
- 対象100件フォルダを `watch` に向けたDBで開く。
- `scan_start` から `scan_end` までを走査時間とする。
- `thumb_start` / `thumb_end` の件数と経過時間を見る。

## ログ回収先
- ベンチログ:
  - `%LOCALAPPDATA%\IndigoMovieManager_bench_upstream_current\logs\bench-runtime.log`
- 生成サムネ:
  - `<upstream-repo-root>/bench_output/upstream_current_bench/Thumb`

## 見るポイント
- `open_datafile_end elapsed_ms=...`
- `scan_end elapsed_ms=...`
- `thumb_end elapsed_ms=...`
- `hash_end elapsed_ms=...`

## 注意
- 上流現行は GUI 操作で `.wb` を開く必要がある。
- `system.thum` は固定してあるので、起動ディレクトリが違ってもサムネ出力先はぶれない。
- フォーク現行はまだ実装中なので、この時点では比較対象に入れない。
