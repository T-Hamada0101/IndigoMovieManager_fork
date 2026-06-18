# Implementation Plan 抜本UI分割ロードマップ 2026-06-18

最終更新日: 2026-06-18

## 1. 目的

IndigoMovieManager の `MainWindow` を、入力、検索、スクロール、選択、Player 操作が背後処理に押し負けない UI shell へ近づける。

この計画の目的は「見た目の大改造」ではなく、UI スレッドに残る責務を短い snapshot / request / apply に分け、重い判断と I/O が code-behind に戻りにくい構造を作ること。

## 2. 現在位置

2026-06-18 時点で、ストレスなし操作 UI 分割 v1 / v1.1 は完了している。

- `Views/Main/MovieViewReadModelBuilder.cs` へ検索 / sort / changed-path ReadModel 計算を分離した。
- `Views/Main/MainWindow.MovieViewReadModel.cs` へ一覧 snapshot、ReadModel apply、sort-only UI 反映を分離した。
- `Views/Main/MainWindow.MovieViewRequests.cs` へ ReadModel 要求制御を分離した。
- `Views/Main/MainWindow.MovieRecordFactory.cs` へ表示レコード生成境界を分離した。
- `Views/Main/MainWindow.MainDbRuntime.cs` へ MainDB runtime 境界を分離した。
- `Views/Main/MainWindow.Lifecycle.cs` へ ContentRendered、startup auto open、終了 drain を分離した。
- `Views/Main/MainWindow.DockLayout.cs` へ dock layout 復元 / 保存 / backup と必須 bottom tab repair を分離した。
- `Views/Main/MainWindow.xaml.cs` は約 904 行まで縮み、constructor、不可避の Window event 配線、入力 / メニュー routing が中心になった。

## 3. 禁止線

- WPF 一覧は維持する。
- WebView2 一覧化、IPC / sidecar 先行導入、`.wb` スキーマ変更、`MainWindow` 全面置換は行わない。
- callback service、汎用 applier class、DI container 導入で抽象化を増やさない。
- `Refresh()`、`Items.Refresh()`、`FilterAndSort(..., true)` の許容箇所を増やさない。
- Release 実機ログを削らない。速度判断は `debug-runtime.log` で説明できる状態を基準にする。
- 未コミット差分を勝手に戻さない。フェーズごとに小さく進める。

## 4. 分割の判断基準

移動してよいもの:

- UI control を直接触らず、入力 snapshot と戻り結果だけで説明できる処理。
- 既に source policy test で禁止線を固定できる処理。
- partial へ移すだけで呼び出し順、ログ、revision guard、cancellation が変わらない処理。

移動を急がないもの:

- XAML event handler そのもの。
- `Refresh()` 互換が残る選択詳細 refresh 入口。
- DB 切替直後の安全復旧や startup fallback など、意味論が壊れるとユーザーDBに影響する処理。

## 5. フェーズ計画

### Phase 0. 境界監査と source policy の土台

目的:

- `MainWindow.xaml.cs` に残る責務を、実装前に source policy で見える形にする。

作業:

- `MainWindowFilterSortExecutionPolicyTests` に加え、必要なら `MainWindowUiSplitSourcePolicyTests` を追加する。
- `MainWindow.xaml.cs` に残す許容入口を明示する。
  - `ComboSort_SelectionChanged(...)`
  - `RefreshSelectionDetailAfterCollectionApplyIfNeeded(...)`
  - `FilterAndSort(..., true)` の既存 2 箇所
  - 直書き `Refresh();` の既存 2 箇所
- 新規 partial へ移したメンバーが `MainWindow.xaml.cs` へ戻らないことを固定する。

完了条件:

- source policy test が `MainWindow.xaml.cs` の責務逆流を検出できる。
- `git diff --check` が通る。

### Phase 1. ReadModel 要求制御 partial の分離

目的:

- `MainWindow.xaml.cs` から一覧要求制御を外し、DB 読込と UI apply の間の流れを追いやすくする。

移動先:

- `Views/Main/MainWindow.MovieViewRequests.cs`

移動候補:

- `FilterAndSort(...)`
- `FilterAndSortAsync(...)`
- `RefreshMovieViewFromCurrentSourceAsync(...)`
- `RefreshMovieViewAfterRenameAsync(...)`
- `BeginFilterAndSortCancellation(...)`
- `ResolveFilterSortExecutionRouteLabel(...)`
- `ResolveFilterSortFullReloadReason(...)`
- `DoesSearchDependOnDirtyFields(...)`
- `DoesCurrentSortDependOnDirtyFields(...)`
- `ShouldRunFilterSortOnBackground(...)`
- `ShouldUseFastAsciiSearchProjection(...)`

維持すること:

- `request_revision`
- cancellation
- `snapshot_ms`
- `filter canceled: ... stage=db-reload`
- `apply-dispatch` cancellation
- `readmodel apply` ログ

完了条件:

- `MainWindow.xaml.cs` に `FilterAndSortAsync(...)` / `RefreshMovieViewFromCurrentSourceAsync(...)` の定義が残らない。
- `MovieViewReadModelBuilder` は Dispatcher / WPF / `ObservableCollection` を知らないまま。
- focused test と Release x64 build が通る。

### Phase 2. 表示レコード生成境界の分離

目的:

- DB row から `MovieRecords` を作る処理と、UI への反映を分ける。

移動先:

- `Views/Main/MainWindow.MovieRecordFactory.cs`

移動候補:

- `MovieRecordBulkBuildContext`
- `MovieRecordBulkBuildCache`
- `DataRowToViewData(...)`
- `CreateMovieRecordFromDataRow(...)`
- `CaptureMovieRecordBulkBuildContext(...)`
- `BuildMovieRecordBulkBuildCache(...)`
- `BuildThumbnailFileNameLookup(...)`
- `ResolveThumbnailDisplayPath(...)`
- `SetRecordsToSource(...)`
- `QueueMovieExistsRefresh(...)`
- `ApplyMovieExistsRefreshBatchAsync(...)`

方針:

- まず partial 分離に留める。
- その後、純粋変換だけを小さな helper へ出すか判断する。
- `MovieRecords` を別 DTO へ全面置換しない。

完了条件:

- `MainWindow.xaml.cs` に `DataRowToViewData(...)` / `CreateMovieRecordFromDataRow(...)` の定義が残らない。
- 単発追加、起動全件変換、movie exists 後追い更新のログと guard が維持される。

### Phase 3. MainDB runtime 境界の分離

目的:

- DB 切替、system/history/watch table 読込、ヘッダー件数更新を UI shell から分ける。

移動先:

- `Views/Main/MainWindow.MainDbRuntime.cs`
- 既存 `Views/Main/MainWindow.DbSwitch.cs` との重複を見て、必要なら名前を整理する。

移動候補:

- `OpenDatafile(...)`
- `ShutdownCurrentDb()`
- `BootNewDb(...)`
- `ApplyColdStartSystemDefaults()`
- `SelectSystemTable(...)`
- `ApplyRuntimeSystemValue(...)`
- `UpsertSystemDataRow(...)`
- `QueueSearchHistoryReload(...)`
- `ReloadSearchHistoryForDbSwitchAsync(...)`
- `GetSystemTable(...)`
- `GetWatchTable(...)`
- `UpdateSort(...)`
- `UpdateSkin(...)`
- `SwitchTab(...)`
- 登録件数 header refresh 系

方針:

- DB 切替の意味論を変えない。
- preflight、旧 DB 維持、後着 DB path guard、no-persist 診断を壊さない。
- `.wb` スキーマは変えない。
- `ApplySearchHistoryRecords(...)` / 履歴候補 UI 差し替えは `Views/Main/MainWindow.Search.cs` に置き、MainDB runtime は DB 切替起点の予約 / 背景読込だけを持つ。

完了条件:

- DB 切替 focused test と Release x64 build が通る。
- no-persist 診断導線の保存 skip ログが維持される。

### Phase 4. 起動 / dock layout / window lifecycle の分離

目的:

- `MainWindow.xaml.cs` から起動後処理、dock layout 復元、終了 drain を分ける。

移動先:

- `Views/Main/MainWindow.Lifecycle.cs`
- `Views/Main/MainWindow.DockLayout.cs`

移動候補:

- `MainWindow_ContentRendered(...)`
- `QueueStartupAutoOpenLastDocSwitch(...)`
- `RunStartupAutoOpenLastDocSwitchAsync(...)`
- `MainWindow_Closing(...)`
- `TryRestoreDockLayout(...)`
- `RunRestoreDockLayoutAsync(...)`
- `TryRestoreDockLayoutFromFile(...)`
- `LoadDockLayoutRestoreText(...)`
- `TryDeserializeDockLayoutText(...)`
- `EnsureRequiredBottomTabsPresent(...)`
- `RestoreWindowBoundsSafely(...)`
- shutdown wait helpers

完了条件:

- first-page / input ready / heavy services / shutdown drain のログが維持される。
- dock layout fallback と backup の互換が維持される。

### Phase 5. 入力 / イベント routing の薄化

目的:

- XAML event handler を、短い guard と request 呼び出しだけへ寄せる。

移動先:

- `Views/Main/MainWindow.InputRouting.cs`

移動候補:

- `OnPreviewTextInput(...)`
- `OnPreviewTextInputStart(...)`
- `OnPreviewTextInputUpdate(...)`
- `Tab_PreviewKeyDown(...)`
- `ComboSort_SelectionChanged(...)`
- menu toggle 系

方針:

- XAML handler 名は変えない。
- event handler から DB read / file I/O / full reload を直接呼ばない。
- sort 変更時の起動 partial fallback は残す。

完了条件:

- `ComboSort_SelectionChanged(...)` が段階ロード中 fallback と通常 sort-only の分岐だけを持つ。
- source policy が `FilterAndSort(..., true)` の許容 2 箇所を維持する。

### Phase 6. Application Core 境界の導入

目的:

- partial 分離後、純粋判断だけを WPF 非依存 helper へ移す。

候補:

- ReadModel request DTO
- DB switch plan DTO
- Dock layout validation policy
- Header count refresh policy
- Input routing policy

禁止:

- 大きな service graph を作らない。
- `MainWindow` と同じ責務を持つ新しい巨大 core を作らない。
- Dispatcher、WPF control、`ObservableCollection`、WebView2 DOM を core へ入れない。

完了条件:

- core helper は unit test 可能で、UI thread を必要としない。
- UI apply は引き続き `MainWindow.MovieViewReadModel.cs` など View 境界に閉じる。

## 6. テスト計画

各フェーズ共通:

```powershell
dotnet test Tests\IndigoMovieManager.Tests\IndigoMovieManager.Tests.csproj -c Release -p:Platform=x64 -p:UseSharedCompilation=false -p:BaseOutputPath=%TEMP%\imm-ui-split-test-output --filter "FullyQualifiedName~MainWindowFilterSortExecutionPolicyTests|FullyQualifiedName~WatchDeferredUiReloadPolicyTests|FullyQualifiedName~MainWindowViewModelFilteredMovieRecsTests|FullyQualifiedName~ExternalSkinHeaderChromePolicyTests|FullyQualifiedName~MainDbSwitchPolicyTests" --no-restore -m:1
```

必要に応じて追加:

- `MainWindowUiIoDeferralSourceTests`
- `MainWindowSettingsPersistencePolicyTests`
- `MainWindowUiSplitSourcePolicyTests`

Release build:

```powershell
dotnet build IndigoMovieManager.sln --configuration Release -p:Platform=x64 -p:UseSharedCompilation=false -p:BaseOutputPath=%TEMP%\imm-ui-split-build-output -m:1 --no-restore
```

仕上げ:

- `git diff --check`
- 更新ドキュメントの UTF-8 BOMなし + LF 確認
- 新規 partial が未追跡のまま取りこぼされないことを `git status --short` で確認

## 7. 実機ログ確認

抜本 UI 分割の完了判定は、コードが分かれただけでは足りない。

確認するログ:

- `filter start/end`
- `sort start/end`
- `readmodel apply begin/end`
- `snapshot_ms`
- `apply_ms`
- `first-page shown`
- `input ready`
- `refresh end active=True`
- `host_navigate_ms`
- `navigate_to_string_ms`
- `navigate_skipped_current`
- `navigate_skip_reason`

active skin 採取は、引き続きコピー `.wb` と `INDIGO_DIAGNOSTIC_NO_PERSIST=1` / `INDIGO_DIAGNOSTIC_STARTUP_DB=<コピー.wb>` を使う。ユーザーDB上で外部 skin を有効化しない。

## 8. 完了の定義

抜本 UI 分割は、次を満たした時に完了扱いにする。

- `MainWindow.xaml.cs` は constructor、不可避の XAML event shell、共有 field、短い lifecycle 接続だけを主に持つ。
- 一覧 ReadModel 計算、一覧 apply、表示レコード生成、DB runtime、dock layout、入力 routing がそれぞれ追えるファイルへ分かれている。
- source policy test が、`Refresh()` / `Items.Refresh()` / `FilterAndSort(..., true)` の禁止線と partial 逆流を検出できる。
- focused test と Release x64 build が通る。
- 実機 `debug-runtime.log` で、入力、検索、sort、watch、skin refresh の支配要因を説明できる。

## 9. 次に着手する最小単位

次の実装は Phase 5 を推奨する。

理由:

- Phase 1 から Phase 4 で、一覧要求制御、表示レコード生成、DB runtime、起動 / dock layout / window lifecycle は partial 分離済み。
- `MainWindow.xaml.cs` に残る主な重さは `ComboSort_SelectionChanged(...)`、`Tab_PreviewKeyDown(...)`、メニュー / 入力イベント routing である。
- Phase 5 は XAML event handler 名と互換入口を変えず、短い guard と request 呼び出しへ寄せる範囲で小さく進める。

## 10. 2026-06-18 Phase 1 実施結果

実施内容:

- `Views/Main/MainWindow.MovieViewRequests.cs` を追加し、ReadModel 要求制御を `MainWindow.xaml.cs` から分離した。
- `FilterAndSort(...)`、`FilterAndSortAsync(...)`、`RefreshMovieViewFromCurrentSourceAsync(...)`、`RefreshMovieViewAfterRenameAsync(...)`、`BeginFilterAndSortCancellation(...)`、`ResolveFilterSortExecutionRouteLabel(...)`、`ResolveFilterSortFullReloadReason(...)`、`DoesSearchDependOnDirtyFields(...)`、`DoesCurrentSortDependOnDirtyFields(...)`、`ShouldRunFilterSortOnBackground(...)`、`ShouldUseFastAsciiSearchProjection(...)` を新 partial へ移した。
- `ComboSort_SelectionChanged(...)` と `RefreshSelectionDetailAfterCollectionApplyIfNeeded(...)` は XAML event / 互換 refresh 入口として `MainWindow.xaml.cs` に残した。
- `FilterAndSort(..., true)`、直書き `Refresh();`、`Items.Refresh()` の許容線は増やしていない。
- `LaneBFacadeGuardArchitectureTests` は `FilterAndSortAsync(...)` の read facade 契約を `MainWindow.MovieViewRequests.cs` で見るようにした。

親レビュー:

- 全体観点レビューでは、Phase 1 は正本どおり「要求制御 partial への分離」に限定する判断で妥当とした。
- シンプル実装レビューでは、新 service / applier / callback / DI を作らず partial 分離だけに留める判断で妥当とした。
- 親レビューでは、DB reload、`movieData` 寿命、`SetRecordsToSource(...)`、revision guard、`apply-dispatch` cancellation、`snapshot_ms` / `apply_ms` ログの順序維持を確認した。

検証:

- focused test: 169 件成功。
- Release x64 build: 成功。`NETSDK1206` 警告 2 件は既存の SQLitePCLRaw RID 警告。
- `git diff --check`: 成功。
- 更新ドキュメントと新規 partial は UTF-8 BOMなし + LF。

次の推奨:

- Phase 2 は完了済み。次は Phase 3 として `Views/Main/MainWindow.MainDbRuntime.cs` を追加し、DB runtime 境界を分ける。

## 11. 2026-06-18 Phase 2 実施結果

実施内容:

- `Views/Main/MainWindow.MovieRecordFactory.cs` を追加し、表示レコード生成境界を `MainWindow.xaml.cs` から分離した。
- `MovieRecordBulkBuildContext`、`MovieRecordBulkBuildCache`、`DataRowToViewData(...)`、`CreateMovieRecordFromDataRow(...)`、`CaptureMovieRecordBulkBuildContext(...)`、`BuildMovieRecordBulkBuildCache(...)`、`BuildThumbnailFileNameLookup(...)`、`ResolveThumbnailDisplayPath(...)`、`SetRecordsToSource(...)`、`QueueMovieExistsRefresh(...)`、`ApplyMovieExistsRefreshBatchAsync(...)` を新 partial へ移した。
- `MainWindow.Startup.cs` の startup first-page / partial feed 専用変換は今回は残した。通常 DataRow 変換と起動 feed 変換は意味論が違うため、安易に共通化しない。
- `FilterAndSort(..., true)`、直書き `Refresh();`、`Items.Refresh()` の許容線は増やしていない。

親レビュー:

- 全体観点レビューでは、移動対象は Phase 2 と整合し、`SetRecordsToSource(...)` / `QueueMovieExistsRefresh(...)` / `ApplyMovieExistsRefreshBatchAsync(...)` は将来 helper 化する場合も `MainVM.ReplaceMovieRecs`、Dispatcher、revision guard を外へ漏らさないことを確認した。
- シンプル実装レビューでは、新 service / interface / DI / DTO 全面置換を作らず partial 分離だけに留める判断で妥当とした。
- 親レビューでは、`DataRowToViewData(...)` の DB path guard、`SetRecordsToSource(...)` の source apply revision guard、movie exists 後追い更新の revision guard を維持したことを確認した。

検証:

- focused test: 171 件成功。
- Release x64 build: 成功。`NETSDK1206` 警告 2 件は既存の SQLitePCLRaw RID 警告。
- `git diff --check`: 成功。
- 更新ドキュメントと新規 partial は UTF-8 BOMなし + LF。

次の推奨:

- Phase 3 として `Views/Main/MainWindow.MainDbRuntime.cs` を追加し、DB 切替、system/history/watch table 読込、ヘッダー件数更新の境界を分ける。

## 12. 2026-06-18 Phase 3 実施結果

実施内容:

- `Views/Main/MainWindow.MainDbRuntime.cs` を追加し、MainDB runtime 境界を `MainWindow.xaml.cs` から分離した。
- `ResetMainHeaderCounts(...)` / `QueueRegisteredMovieCountRefresh(...)` / `TryAdjustRegisteredMovieCount(...)` / `RefreshRegisteredMovieCountAsync(...)`、`OpenDatafile(...)`、`ShutdownCurrentDb()`、`BootNewDb(...)`、`ApplyColdStartSystemDefaults()`、`SelectSystemTable(...)`、`ApplyRuntimeSystemValue(...)`、`UpsertSystemDataRow(...)`、`QueueSearchHistoryReload(...)`、`ReloadSearchHistoryForDbSwitchAsync(...)`、`GetSystemTable(...)`、`GetWatchTable(...)`、`GetWatchTableSnapshot(...)`、`UpdateSort(...)`、`UpdateSkin(...)`、`SwitchTab(...)` を新 partial へ移した。
- `Views/Main/MainWindow.DbSwitch.cs` は `TrySwitchMainDb(...)`、preflight、pre/post switch、Recent / LastDoc、旧 QueueDB pending 掃除を持つ切替フローとして維持した。
- `ApplySearchHistoryRecords(...)` / `ApplySearchHistoryRecordItems(...)` / `AreSameSearchHistoryRecords(...)` は `Views/Main/MainWindow.Search.cs` へ置き、MainDB runtime は DB 切替起点の履歴読込予約だけを持つ。
- `BeginExternalSkinHostRefreshBatch("dbinfo-DBFullPath")`、`ApplySkinByName(skin, persistToCurrentDb: false)`、system preflight 受け渡し、watch table normalize、登録件数 revision guard、`DispatcherPriority.Background` は維持した。

親レビュー:

- 全体観点レビューでは、`DbSwitch.cs` を切替トランザクション、`MainDbRuntime.cs` を boot / shutdown / table / header runtime として分ける判断で妥当とした。
- シンプル実装レビューでは、新 service / interface / DI / policy class を作らず partial 分離だけに留める判断で妥当とした。
- 親レビューでは、preflight 済み `SystemData` の `OpenDatafile -> BootNewDb -> GetSystemTable` 受け渡し、旧 DB 停止前検証、no-persist 診断の保存 skip 境界、skin persist 抑止、検索履歴 UI apply の置き場所を確認した。

検証:

- focused test: 189 件成功。
- Release x64 build: 成功。`NETSDK1206` 警告 2 件は既存の SQLitePCLRaw RID 警告。
- `git diff --check`: 成功。
- 更新ドキュメントと新規 partial は UTF-8 BOMなし + LF。

次の推奨:

- Phase 4 として `MainWindow.Lifecycle.cs` / `MainWindow.DockLayout.cs` を追加し、起動、dock layout、window lifecycle の境界を分ける。

## 13. 2026-06-18 Phase 4 実施結果

実施内容:

- `Views/Main/MainWindow.Lifecycle.cs` を追加し、`MainWindow_ContentRendered(...)`、startup auto open、`INDIGO_DIAGNOSTIC_STARTUP_DB` 診断入口、`MainWindow_Closing(...)`、shutdown wait helper を `MainWindow.xaml.cs` から分離した。
- `Views/Main/MainWindow.DockLayout.cs` を追加し、dock layout restore / load / deserialize / validation / backup / save、必須 bottom tab repair、`RestoreWindowBoundsSafely(...)` を分離した。
- `MainWindow.xaml.cs` は constructor、イベント接続、不可避の Window shell、入力 / メニュー routing を中心に残し、`ContentRendered += MainWindow_ContentRendered;`、`Closing += MainWindow_Closing;`、`TryRestoreDockLayout();`、`EnsureRequiredBottomTabsPresent();` の呼び出し順は維持した。
- first-page / input ready / heavy services、startup auto open snapshot guard、no-persist 保存 skip、watcher shutdown drain、dock layout fallback / backup のログ契約は変えていない。
- service 化、DI 化、LifecycleManager / DockLayoutManager 化は行わず、partial 分離に留めた。

親レビュー:

- 全体観点レビューでは、移動対象は Phase 4 と整合し、first-page / input-ready、startup auto open、no-persist、Closing drain、dock layout fallback / backup の意味論を変えていないことを確認した。
- シンプル実装レビューでは、manager service や汎用 shutdown list を作らず、constructor の配線と既存順序を維持する判断で妥当とした。
- 親レビューでは、source policy の旧 `MainWindow.xaml.cs` 参照を `MainWindow.Lifecycle.cs` / `MainWindow.DockLayout.cs` / `MainWindow.MainDbRuntime.cs` へ追従する必要を確認し、テスト側を更新した。

検証:

- focused test: 199 件成功。
- Release x64 build: 成功。`NETSDK1206` 警告 2 件は既存の SQLitePCLRaw RID 警告。
- `git diff --check`: 成功。
- 更新ドキュメントと新規 partial は UTF-8 BOMなし + LF。

次の推奨:

- Phase 5 として入力 / イベント routing を薄くし、`ComboSort_SelectionChanged(...)`、`Tab_PreviewKeyDown(...)`、メニュー系イベントを短い guard と既存 request 呼び出しへ寄せる。
