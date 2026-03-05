# Implementation Plan（再計画版: メインタブ「サムネ失敗」追加 + 最終失敗動画一覧 DataGrid, 2026-03-05）

## 0. 背景
- サムネイル処理の最終失敗動画を、運用中に即座に俯瞰できるUIがない。
- 現状はログや個別調査が必要で、再試行判断や原因分析に時間がかかる。
- 失敗件数は全体の約 `1/1000`（0.1%）想定で、通常は少件数運用。

## 1. 目的
- メインタブに `サムネ失敗` タブを追加する。
- QueueDBの `Status=Failed`（最終状態）を全件一覧表示する。
- DataGridに調査必要情報を欠損なく表示する。

## 2. 要件（確定）
- 対象は「現在開いているMainDBに紐づくQueueDB」の `Status=Failed` のみ。
- 最終失敗の定義:
  - `UNIQUE(MainDbPathHash, MoviePathKey, TabIndex)` の現行行が `Failed`。
- 表示列（全情報）:
  - `QueueId`, `MainDbPathHash`, `MoviePath`, `MoviePathKey`, `TabIndex`, `MovieSizeBytes`
  - `ThumbPanelPos`, `ThumbTimePos`, `Status`, `AttemptCount`, `LastError`
  - `OwnerInstanceId`, `LeaseUntilUtc`, `CreatedAtUtc`, `UpdatedAtUtc`
- 初版はシンプル実装:
  - 反映は全置換（`Clear + Add`）。
  - 差分更新は将来拡張。
- 重要:
  - `サムネ失敗` タブが非選択時は再読込しない（dirtyフラグのみ更新）。
  - タブ選択時に dirty なら即時1回再読込する。

## 3. スコープ
- IN
  - QueueDB失敗一覧取得API
  - ViewModelコレクション追加
  - MainWindowタブ + DataGrid追加
  - 非同期更新・DB切替競合ガード・dirty制御
- OUT
  - 自動再試行ロジック変更
  - QueueDBスキーマ変更
  - サムネイルエンジン失敗判定変更

## 4. 設計方針
- 取得元はQueueDBを正とする（UI一時プレースホルダは使わない）。
- UIブロック回避のため、取得はバックグラウンド実行。
- 失敗少件数前提で初版は全置換反映。
- DataGrid仮想化を有効化し、将来増加にも備える。
- エンジン分離方針を維持し、Queue/EngineへUI依存を持ち込まない。

## 5. 実装詳細

### 5.1 QueueDB取得API
- `QueueDbService` に `GetFailedItems()` を追加（戻り値 `List<QueueDbFailedItem>`）。
- SQL:
  - `MainDbPathHash = current`
  - `Status = Failed`
  - `ORDER BY UpdatedAtUtc DESC, QueueId DESC`
- DTOは表示15項目を保持。

### 5.2 更新コーディネータ（明文化）
- `MainWindow` 側に以下を追加:
  - `bool _thumbnailFailedTabSelected`
  - `bool _thumbnailFailedListDirty`
  - `int _thumbnailFailedRefreshRevision`
  - `int _thumbnailFailedAppliedRevision`
- ルール:
  - 変更イベント時は `MarkThumbnailFailedListDirty()` を呼ぶ。
  - タブ非選択なら dirty のみ立てて終了。
  - タブ選択ならデバウンス後に1回再読込。

### 5.3 トリガー仕様（固定）
- dirtyを立てる契機:
  - `onJobCompleted`（成功/失敗問わず）
  - `ResetFailedThumbnailJobsForCurrentDb()` 実行後
  - MainDBオープン/切替時
- 実際に再読込する契機:
  - `サムネ失敗` タブ選択時（dirtyなら即時）
  - タブ選択中のデバウンス消化時
- 常時ポーリングはしない。

### 5.4 refreshRevision運用（固定）
- `Interlocked.Increment(ref _thumbnailFailedRefreshRevision)` を実行する契機:
  - MainDBオープン/切替確定時
  - ResetFailed実行時
  - 明示的手動再読込時（将来ボタン追加時）
- 非同期取得開始時:
  - `requestedRevision` と `requestedMainDbPath` を取得して保持。
- UI反映前:
  - 現在値と比較し不一致なら破棄。
  - 一致時のみ適用し `appliedRevision = requestedRevision`。

### 5.5 UI（MainWindow.xaml）
- `Tabs` に `TabItem Header="サムネ失敗"` を追加。
- タブ内 `DataGrid` を失敗一覧へバインド。
- 仮想化:
  - `EnableRowVirtualization=True`
  - `EnableColumnVirtualization=True`
  - `VirtualizingPanel.IsVirtualizing=True`
  - `VirtualizingPanel.VirtualizationMode=Recycling`
- 列は自動生成せず固定定義。

### 5.6 ViewModel
- `MainWindowViewModel` に `ObservableCollection<ThumbnailFailedRecordViewModel>` を追加。
- `BindingOperations.EnableCollectionSynchronization` を有効化。
- 反映は初版全置換。

### 5.7 エラー処理
- QueueDB取得失敗は空一覧 + ログで継続。
- `MainDB未選択` は空一覧で早期return。

## 6. タスクリスト

| ID | 状態 | タスク | 対象ファイル | 完了条件 |
|---|---|---|---|---|
| FAILTAB-001 | 未着手 | 失敗一覧DTOと取得API追加 | `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs` | `Failed` 行のみ取得できる |
| FAILTAB-002 | 未着手 | 失敗一覧コレクション追加 | `ModelViews/MainWindowViewModel.cs` | バインド可能な公開プロパティ追加 |
| FAILTAB-003 | 未着手 | `サムネ失敗` タブ + DataGrid追加 | `MainWindow.xaml` | 全列が表示される |
| FAILTAB-004 | 未着手 | 非同期取得 + 全置換反映実装 | `MainWindow.xaml.cs` | UI操作を阻害しない |
| FAILTAB-005 | 未着手 | dirty制御（非選択時は再読込抑止）実装 | `MainWindow.xaml.cs` | 非選択時にDB再読込しない |
| FAILTAB-006 | 未着手 | refreshRevision競合ガード実装 | `MainWindow.xaml.cs` | 旧DB結果が混在しない |
| FAILTAB-007 | 未着手 | トリガー接続（onJobCompleted/Reset/DB切替/タブ選択） | `Thumbnail/MainWindow.ThumbnailCreation.cs` `Thumbnail/MainWindow.ThumbnailQueue.cs` `MainWindow.xaml.cs` | 更新発火が仕様通り |
| FAILTAB-008 | 未着手 | テスト追加（取得/並び順/dirty/デバウンス/DB切替破棄） | `Tests/IndigoMovieManager_fork.Tests/*` | 主要回帰を自動検知 |

## 7. 検証項目
- 失敗0件: 空DataGridで崩れない。
- 失敗少数（1〜50件）: タブ選択時に即更新される。
- タブ非選択時: `onJobCompleted` 連打でも再読込されない（dirtyのみ）。
- タブ選択中: 連続イベントでもデバウンスで過剰再読込しない。
- `Failed -> Pending`（Reset後）で一覧から消える。
- DB切替中に旧要求が完了しても結果が破棄される。

## 8. リスクと対策
- リスク: 失敗件数増で全置換が重くなる
  - 対策: 閾値（例: 500件）超過で差分更新へ切替可能な構造で実装。
- リスク: 更新取りこぼし
  - 対策: dirtyフラグ方式で、非選択中イベントを選択時に必ず回収。
- リスク: DB切替直後の取り違え
  - 対策: `requestedMainDbPath` + `requestedRevision` 一致時のみ反映。
