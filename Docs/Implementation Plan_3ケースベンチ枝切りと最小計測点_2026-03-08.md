# 2ケース先行ベンチ 枝切りと最小計測点（2026-03-08）

## 1. 目的
- 今すぐは `709a137` と本家現行の2ケースを、同一データセット `D:\BentchItem_HDD` で比較できる状態にする。
- フォーク現行は実装安定後に第2段階で追加する。
- 最初から完全自動化を狙わず、まずは同じ粒度の計測点をそろえる。

## 2. 基本方針
- 1つの作業ツリーを切り替え回さない。
- ケースごとに別ディレクトリか別 worktree を持つ。
- 最小計測点は `起動` `DBオープン` `初回走査` `サムネ1件` `サムネ複数件` `ハッシュ` の6つに絞る。

## 3. 推奨ディレクトリ構成

### 3.1 フォーク初期原型
- 元リポジトリ: `C:\Users\na6ce\source\repos\IndigoMovieManager_fork`
- 作業先候補: `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_bench_709a137`
- ブランチ名: `codex/bench/709a137-baseline`

### 3.2 本家現行
- 元リポジトリ: `C:\Users\na6ce\source\repos\IndigoMovieManager`
- 作業先候補: `C:\Users\na6ce\source\repos\IndigoMovieManager_bench_upstream`
- ブランチ名: `codex/bench/upstream-current-baseline`

### 3.3 フォーク現行（第2段階）
- 元リポジトリ: `C:\Users\na6ce\source\repos\IndigoMovieManager_fork`
- 作業先候補: `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_bench_current`
- ブランチ名: `codex/bench/fork-current`
- 備考: 実装が落ち着くまでは切らない

## 4. 枝切り手順

### 4.1 フォーク初期原型
PowerShell例:

```powershell
cd C:\Users\na6ce\source\repos\IndigoMovieManager_fork
git -c safe.directory='C:/Users/na6ce/source/repos/IndigoMovieManager_fork' switch --detach 709a137
git -c safe.directory='C:/Users/na6ce/source/repos/IndigoMovieManager_fork' switch -c codex/bench/709a137-baseline
```

別ディレクトリでやるなら、先にコピーまたは worktree を用意する。

### 4.2 本家現行
```powershell
cd C:\Users\na6ce\source\repos\IndigoMovieManager
git switch master
git switch -c codex/bench/upstream-current-baseline
```

### 4.3 フォーク現行（第2段階）
```powershell
cd C:\Users\na6ce\source\repos\IndigoMovieManager_fork
git -c safe.directory='C:/Users/na6ce/source/repos/IndigoMovieManager_fork' switch master
git -c safe.directory='C:/Users/na6ce/source/repos/IndigoMovieManager_fork' switch -c codex/bench/fork-current
```

実装が一段落してから実施する。

## 5. 最小計測点

### 5.1 共通で欲しい時刻
- `app_start`
- `db_open_start`
- `db_open_end`
- `scan_start`
- `scan_end`
- `thumb_start`
- `thumb_end`
- `hash_start`
- `hash_end`

### 5.2 共通で欲しい付加情報
- `case_name`
- `db_path`
- `mode`
- `movie_path`
- `movie_count`
- `success`
- `error`

## 6. ケース別の差し込み位置

### 6.1 709a137 初期原型
対象ファイル:
- `MainWindow.xaml.cs`
- `Tools.cs`

差し込み候補:
- `MainWindow.xaml.cs`
  - `OpenDatafile(string dbFullPath)` の先頭と末尾
  - `CheckFolderAsync(CheckMode mode)` の先頭と末尾
  - `CheckThumbAsync()` のループ取り出し前後ではなく、まずは `CreateThumbAsync` の前後だけでよい
  - `CreateThumbAsync(QueueObj queueObj, bool IsManual = false)` の先頭と末尾
- `Tools.cs`
  - `GetHashCRC32(string filePath = "")` の先頭と return 直前

理由:
- 初期原型は `MainWindow.xaml.cs` に処理が集中している。
- ここに軽いログを足すのが最短。

### 6.2 本家現行
対象ファイル:
- `MainWindow.xaml.cs`
- `MainWindow.Watcher.cs`
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/Tools.cs`

差し込み候補:
- `MainWindow.xaml.cs`
  - `OpenDatafile(string dbFullPath)` の先頭と末尾
- `MainWindow.Watcher.cs`
  - `CheckFolderAsync(CheckMode mode)` の先頭と末尾
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - `CheckThumbAsync(CancellationToken ...)` の開始と終了
  - `CreateThumbAsync(...)` の先頭と末尾
- `Thumbnail/Tools.cs`
  - `GetHashCRC32(...)` の先頭と return 直前

理由:
- 本家現行はサムネ処理が分離済みなので、初期原型と同じ場所にはいない。

### 6.3 フォーク現行（第2段階）
対象ファイル:
- `MainWindow.xaml.cs`
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- 必要なら `Thumbnail/Tools.cs`

現状:
- `OpenDatafile` は `DebugRuntimeLog.TaskStart/TaskEnd` 済み
- `CheckThumbAsync` は `DebugRuntimeLog.TaskStart/TaskEnd` 済み
- `CreateThumbAsync` は `DebugRuntimeLog.TaskStart/TaskEnd` 済み

追加で欲しいもの:
- `CheckFolderAsync` の前後が揃っていないなら補完する
- `GetHashCRC32` 単体時間を取りたいなら `Thumbnail/Tools.cs` に最小ログを足す

理由:
- フォーク現行は既存ログを再利用できる
- 追加差分は最小で済む

## 7. ログ形式
- まずはテキストログで十分
- 1行1イベントで次の形式に寄せる

```text
2026-03-08 12:34:56.789 [bench] event=db_open_start case=upstream db='...'
2026-03-08 12:35:01.234 [bench] event=db_open_end case=upstream elapsed_ms=4445 success=true
```

最低限、`event` `case` `elapsed_ms` があれば集計できる。

## 8. 保存先
- ケースごとにログルートを分ける
- 推奨:
  - 初期原型: `%LOCALAPPDATA%\IndigoMovieManager_bench_709a137\logs`
  - 本家現行: `%LOCALAPPDATA%\IndigoMovieManager_bench_upstream\logs`
  - フォーク現行: `%LOCALAPPDATA%\IndigoMovieManager_fork\logs`

## 9. 実施順
- 第1段階: `709a137` と本家現行だけ測る
- 第2段階: フォーク現行が安定したら追加する

## 10. 既存スクリプトの扱い

### 10.1 そのまま使えるもの
- フォーク現行の `Thumbnail\Test\run_thumbnail_engine_bench.ps1`
- フォーク現行の `Thumbnail\Test\run_thumbnail_engine_bench_folder.ps1`

### 10.2 そのまま使えないもの
- `709a137` 初期原型
- 本家現行

理由:
- どちらも `IndigoMovieManager_fork.Tests` と現行フォーク専用ログ前提になっているため
- まずは手動計測で差を見る方が速い

## 11. 最初の測定シナリオ

### 11.1 サムネ1件
- `D:\BentchItem_HDD` から小ファイル1本を選ぶ
- DBは空に近い状態で開始する
- 手動または既存の自動作成経路で1件作る
- `thumb_start` から `thumb_end` を見る

### 11.2 初回走査
- `D:\BentchItem_HDD` から100件程度のサブフォルダを使う
- 新規DBを作る
- 監視登録または初回取り込みを実行する
- `scan_start` から `scan_end` を見る

### 11.3 ハッシュ
- 同じ動画1本で `GetHashCRC32` を複数回呼ぶ
- `hash_start` から `hash_end` を見る

## 12. ここでやらないこと
- いきなり3ケース共通の完全自動スクリプトを作る
- 初手から巨大フォルダで回す
- UI差分まで同時に測る

## 13. 次アクション
1. `709a137` と本家現行の作業ディレクトリを用意する
2. 2ケースへ最小計測点を差し込む
3. `D:\BentchItem_HDD` から小ファイル1本でサムネ1件を測る
4. 100件フォルダで初回走査を測る
5. フォーク現行は安定後に同じ手順で追加する