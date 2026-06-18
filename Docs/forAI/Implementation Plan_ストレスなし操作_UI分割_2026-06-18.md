# Implementation Plan ストレスなし操作 UI分割 2026-06-18

最終更新日: 2026-06-18

## 1. 目的

IndigoMovieManager の操作体感を、入力、検索、スクロール、選択、Player 操作が背後処理に押し負けない形へ寄せる。

当面は WPF 一覧を維持し、WebView2 一覧化、IPC / sidecar 先行導入、`.wb` スキーマ変更、`MainWindow` 全面置換は行わない。

## 2. v1 の実装方針

- `MainWindow` は DB 読込、Dispatcher apply、既存互換ガードに集中する。
- `FilterAndSortAsync(...)` と `RefreshMovieViewFromCurrentSourceAsync(...)` の検索 / 並び替え再計算、changed-path 局所更新、fallback reason の計算は `MovieViewReadModelBuilder` へ寄せる。
- `SortDataAsync(...)` の sort-only 計算は v1 では既存経路を維持し、UI 反映だけを共通 apply helper へ通す。
- UI 反映は `TryApplyMovieViewReadModelResultOnUiThread(...)` へ集約し、`SearchCount`、`filterList`、`ReplaceFilteredMovieRecs(...)`、選択詳細 refresh 判定、visible range refresh を同じ入口で扱う。
- 既存テストが使う `MainWindow.TryBuildChangedMovieRefreshSource...` などの内部入口は wrapper として残し、呼び出し側の差分を小さくする。

## 3. 実装内容

- `Views/Main/MovieViewReadModelBuilder.cs` を追加した。
  - `MovieViewReadModelRequest`
  - `MovieViewReadModelResult`
  - `MovieViewReadModelBuilder`
- `FilterAndSortAsync(...)` は full reload 後の filter / sort 計算を builder へ委譲する。
- `RefreshMovieViewFromCurrentSourceAsync(...)` は query-only / watch changed-path 計算を builder へ委譲する。
- watch で拾った `ObservedState` は UI スレッド snapshot 取得時に `MovieRecords` へ反映し、builder は UI モデルを書き換えない。
- `SortDataAsync(...)` は sort-only の UI 反映を共通 apply helper へ通す。
- apply ログへ `request_revision`、`result_count`、`changed`、`update_mode`、`fallback_reason`、`apply_ms` を出す。
- filter / refresh / sort の入口ログへ `snapshot_ms` を出し、UI 側 snapshot 取得の重さを分けて見る。

## 3.1. v1.1 一覧UI適用境界の分離

- `Views/Main/MainWindow.MovieViewReadModel.cs` を追加し、一覧の snapshot 取得、ObservedState 反映入口、ReadModel apply、sort-only UI 反映を `MainWindow.xaml.cs` から分離した。
- `FilterAndSortAsync(...)` と `RefreshMovieViewFromCurrentSourceAsync(...)` は `MainWindow.xaml.cs` に残し、DB 読込、後着キャンセル、builder 呼び出し、Dispatcher apply 待ちの上位フローを維持する。
- `ComboSort_SelectionChanged(...)` と `RefreshSelectionDetailAfterCollectionApplyIfNeeded(...)` は既存 UI イベント / 互換 refresh の入口として `MainWindow.xaml.cs` に残す。
- callback service、汎用 applier class、DI 化は入れない。partial 分離だけで責務境界を締める。
- source policy test で、新 partial に snapshot / apply / sort-only 経路があること、`MainWindow.xaml.cs` に `MovieViewReadModelSnapshot` / `TryApplyMovieViewReadModelResultOnUiThread(...)` / `SortDataAsync(...)` が戻らないことを固定する。

## 3.2. Phase 1 ReadModel要求制御 partial の分離

- `Views/Main/MainWindow.MovieViewRequests.cs` を追加し、`FilterAndSortAsync(...)` / `RefreshMovieViewFromCurrentSourceAsync(...)` / cancellation / full reload reason / apply-dispatch cancellation を `MainWindow.xaml.cs` から分離した。
- `ComboSort_SelectionChanged(...)` と選択変化互換 `Refresh()` 入口は `MainWindow.xaml.cs` に残し、XAML event と互換 refresh の意味論を変えない。
- `FilterAndSort(..., true)`、直書き `Refresh();`、`Items.Refresh()` の許容線は増やしていない。
- 次は `MainWindow.MovieRecordFactory.cs` で表示レコード生成境界を分ける。

## 3.3. Phase 2 表示レコード生成境界の分離

- `Views/Main/MainWindow.MovieRecordFactory.cs` を追加し、`DataRowToViewData(...)` / `CreateMovieRecordFromDataRow(...)` / `SetRecordsToSource(...)` / movie exists 後追い更新を `MainWindow.xaml.cs` から分離した。
- `MainWindow.Startup.cs` の startup first-page / partial feed 変換は今回は残し、起動ログと継続状態の意味論を変えない。
- `FilterAndSort(..., true)`、直書き `Refresh();`、`Items.Refresh()` の許容線は増やしていない。
- Phase 2 の次段として `MainWindow.MainDbRuntime.cs` で DB runtime 境界を分けた。

## 3.4. Phase 3 MainDB runtime 境界の分離

- `Views/Main/MainWindow.MainDbRuntime.cs` を追加し、DB session boot / shutdown、system / history / watch table 読込、登録件数 header refresh を `MainWindow.xaml.cs` から分離した。
- `Views/Main/MainWindow.DbSwitch.cs` は `TrySwitchMainDb(...)` 中心の切替フローとして維持し、preflight、旧 DB 維持、Recent / LastDoc、旧 QueueDB pending 掃除を移さない。
- 検索履歴の UI 差し替えは `Views/Main/MainWindow.Search.cs` に残し、MainDB runtime は DB 切替起点の予約 / 背景読込だけを持つ。
- `BeginExternalSkinHostRefreshBatch("dbinfo-DBFullPath")`、`ApplySkinByName(skin, persistToCurrentDb: false)`、登録件数 revision guard、DB path guard、watch table normalize は維持した。
- Phase 3 の次段として `MainWindow.Lifecycle.cs` / `MainWindow.DockLayout.cs` で起動、dock layout、window lifecycle 境界を分けた。

## 3.5. Phase 4 起動 / dock layout / window lifecycle 境界の分離

- `Views/Main/MainWindow.Lifecycle.cs` を追加し、`MainWindow_ContentRendered(...)`、startup auto open、診断 startup DB override、`MainWindow_Closing(...)`、shutdown wait helper を `MainWindow.xaml.cs` から分離した。
- `Views/Main/MainWindow.DockLayout.cs` を追加し、dock layout restore / load / deserialize / validation / backup / save、必須 bottom tab repair、window bounds 復元を分離した。
- `MainWindow.xaml.cs` は constructor、イベント接続、不可避の Window shell、入力 / メニュー routing を中心に残した。
- first-page / input ready / heavy services、no-persist 保存 skip、watcher shutdown drain、dock layout fallback / backup のログ契約は維持した。
- Phase 4 の次段として `MainWindow.InputRouting.cs` で入力 / イベント routing 境界を分けた。

## 3.6. Phase 5 入力 / イベント routing 境界の分離

- `Views/Main/MainWindow.InputRouting.cs` を追加し、IME 入力、left drawer toggle、一覧キー dispatch、sort combo を `MainWindow.xaml.cs` から分離した。
- XAML handler 名と constructor の TextComposition handler 登録名は維持した。
- `ComboSort_SelectionChanged(...)` の段階ロード中 `FilterAndSort(id.ToString(), true)` fallback と通常 `SortDataAsync(...)` 経路は維持した。
- `Tab_PreviewKeyDown(...)` は routing のみを持ち、Player / Tag / Score / Rename / OpenParent / Delete 実処理は既存 partial に残した。
- Phase 5 の次段として `DockLayoutRestorePolicy.cs` で dock layout validation の純粋判断を WPF 非依存 helper へ分けた。

## 3.7. Phase 6-A Dock layout validation policy 境界の分離

- `Views/Main/DockLayoutRestorePolicy.cs` を追加し、保存済み dock layout 文字列の互換判定を `MainWindow.DockLayout.cs` から分離した。
- `FindMissingRequiredDockLayoutReason(...)` と thumbnail error bottom tab 必須判定を helper へ寄せ、layout file read / backup / deserialize / AvalonDock repair は View 境界に残した。
- helper は `System.Windows` / `AvalonDock` / `Dispatcher` / `File` / `Path` / `MainVM` を参照しない。
- Phase 6-A の次段として、登録動画総数 refresh 結果の stale guard を WPF 非依存 helper へ分けた。

## 3.8. Phase 6-B Registered movie count refresh policy 境界の分離

- `Views/Main/RegisteredMovieCountRefreshPolicy.cs` を追加し、登録動画総数 refresh 結果を UI へ反映してよいかを `requestRevision == currentRevision && isCurrentDb` の純粋判断へ分けた。
- `Views/Main/MainWindow.MainDbRuntime.cs` は DB 読込、Dispatcher apply、`MainVM.DbInfo.RegisteredMovieCount` 更新、未初期化時の正確値再取得を持つ View 境界として維持した。
- `TryAdjustRegisteredMovieCount(...)` の `delta == 0` と `Math.Max(0, current + delta)` は MainWindow 側に残し、細かすぎる helper 化を避けた。
- helper は `System.Windows` / `Dispatcher` / `MainVM` / DB read を参照しない。
- Phase 6-B の次段として、sort combo 選択変更時の経路判断を WPF 非依存 helper へ分けた。

## 3.9. Phase 6-C Sort combo selection policy 境界の分離

- `Views/Main/SortComboSelectionPolicy.cs` を追加し、sort combo 選択変更時の実行可否、段階ロード中 full reload fallback、サムネ ERROR 順 refresh 要否を純粋判断へ分けた。
- `Views/Main/MainWindow.InputRouting.cs` は選択値の取り出し、`FilterAndSort(...)`、`SortDataAsync(...)`、`RefreshThumbnailErrorRecords(...)`、`SelectFirstItem()` を持つ View 境界として維持した。
- DB未選択、sort combo suppress、表示レコード0件では、元の早期 return と同じく `MovieRecs.Count` や選択値文字列化を余分に読まない。
- `Tab_PreviewKeyDown(...)` は WPF `Key` / `KeyEventArgs` / `Tabs` 依存が強いため、helper 化しない。
- Phase 6-C の次段として、MainDB 切替時の副作用 plan 判断を WPF 非依存 helper へ分けた。

## 3.10. Phase 6-D Main DB switch side effect plan 境界の分離

- `Views/Main/MainDbSwitchPlanPolicy.cs` を追加し、menu close、旧DB表示状態保存、Recent更新、LastDoc保存、旧Queue pending掃除の実行可否を純粋判断へ分けた。
- `Views/Main/MainWindow.DbSwitch.cs` は DB path 正規化、preflight revision guard、DB schema validation、system table read、`OpenDatafile(...)`、実 side effect 本体を持つ境界として維持した。
- `IsMainDbSwitchPreflightCurrent(...)` は revision と `MainVM` を読む後着 guard のため、helper 化しない。
- helper は `System.Windows` / `Dispatcher` / `DataTable` / `File` / `Directory` / `QueueDbService` / `Properties.Settings` を参照しない。
- 抜本 UI 分割ロードマップの完了監査へ進み、計画範囲では完了扱いにした。

## 3.11. 完了監査

- `Views/Main/MainWindow.xaml.cs` は 766 行。constructor、不可避の Window / XAML event shell、共有 field、互換 refresh helper が中心になっている。
- ReadModel 計算、一覧 apply、要求制御、表示レコード生成、DB runtime、lifecycle、dock layout、入力 routing は専用 partial / helper へ分離済み。
- `FilterAndSort(..., true)` は起動 fallback と段階ロード中 sort fallback の 2 箇所、直書き `Refresh();` は startup first page と選択変化互換 helper の 2 箇所だけ。`Items.Refresh()` は本体コードへ戻っていない。
- 完了監査では、最新ログに sort / watch の新規操作が入っていない弱さを確認した。今回は過去ログで補完し、次回実機採取時は検索入力、sort 変更、watch reload を同じ Release run に入れる。
- MainWindow 全面置換につながる追加分離は行わない。次は実機ログが新しい支配要因を示した時だけ、操作優先境界などの局所施策として扱う。

## 3.12. v1.2 UI 操作優先境界の分離

- `Views/Main/UiOperationPriorityPolicy.cs` を追加し、user-priority、manual mode、watch UI suppression、recent viewport、Player 再生中の判断を WPF 非依存の小さな policy へ寄せた。
- `ComboSort_SelectionChanged(...)` は `BeginUserPriorityWork("sort")` / `EndUserPriorityWork("sort")` の `try/finally` で包み、sort 中の watch / Everything poll は既存 catch-up 経路へ逃がす。
- scroll / PageUp / PageDown は user-priority scope を開かず、250ms の recent viewport interaction として扱う。Everything poll はこの間だけ一周見送り、catch-up reload は積まない。
- Everything poll の defer / interval ログへ `operation_reason`、`defer_reason`、`recent_viewport`、`catch_up`、`poll_delay_ms` を追加し、検索、sort、スクロール、Player 操作中の背後処理延期理由を実機ログで追えるようにした。
- `Refresh()`、`Items.Refresh()`、`FilterAndSort(..., true)` の許容箇所は増やしていない。

## 4. 維持する禁止線

- `Refresh()`、`Items.Refresh()`、`FilterAndSort(..., true)` の許容箇所を増やさない。
- `.wb` は変更しない。
- ユーザーDB上で外部 skin を有効化しない。
- `header-reload` / `fallback-notice-retry` は CatalogRefresh のまま維持し、same-document skip 対象へ広げない。
- UI テンポ改善を理由に Release 実機ログを削らない。

## 5. 検証

focused test:

```powershell
dotnet test Tests\IndigoMovieManager.Tests\IndigoMovieManager.Tests.csproj -c Release -p:Platform=x64 -p:UseSharedCompilation=false -p:BaseOutputPath=%TEMP%\imm-readmodel-test-output --filter "FullyQualifiedName~MainWindowFilterSortExecutionPolicyTests|FullyQualifiedName~WatchDeferredUiReloadPolicyTests|FullyQualifiedName~ExternalSkinHeaderChromePolicyTests|FullyQualifiedName~MainWindowViewModelFilteredMovieRecsTests" --no-restore
```

Release build:

```powershell
dotnet build IndigoMovieManager.sln --configuration Release -p:Platform=x64 -p:UseSharedCompilation=false -p:BaseOutputPath=%TEMP%\imm-readmodel-build-output -m:1 --no-restore
```

確認観点:

- builder 経由でも full filter / query-only refresh / changed-path 局所更新の既存契約が維持される。
- builder が `MovieRecords` を直接書き換えない。
- UI apply が共通 helper に集約される。
- `Items.Refresh()` を本体コードへ戻さない。
- 直書き `Refresh()` と `FilterAndSort(..., true)` の許容箇所が増えない。

2026-06-18 実施結果:

- v1 focused test: 143 件成功。
- v1.1 focused test: 167 件成功。
- Phase 1 focused test: 169 件成功。
- Phase 2 focused test: 171 件成功。
- Phase 3 focused test: 189 件成功。
- Phase 4 focused test: 199 件成功。
- Phase 5 focused test: 197 件成功。
- Phase 6-A focused test: 210 件成功。
- Phase 6-B focused test: 196 件成功。
- Phase 6-C focused test: 199 件成功。
- Phase 6-D focused test: 197 件成功。
- completion focused test: 206 件成功。
- v1.2 UI操作優先境界 focused test: 255 件成功。
- Release x64 build: 成功。
- `git diff --check`: 成功。
- 本ドキュメント: UTF-8 BOMなし、LF。
- active skin 実機ログ: コピーDB + `INDIGO_DIAGNOSTIC_NO_PERSIST=1` / `INDIGO_DIAGNOSTIC_STARTUP_DB=<コピー.wb>` / `INDIGO_DIAGNOSTIC_REPEAT_SKIN_REFRESH=1` で採取成功。
  - 初回: `active=True ready=True reason=dbinfo-DBFullPath host_navigate_ms=772.6 navigate_to_string_ms=171.6`
  - repeat: `reason=dbinfo-Skin errorType=HostNavigateSkippedSameDocument navigate_skipped_current=True navigate_skip_reason='same-document' navigate_to_string_ms=0.0`

## 6. 残確認

- 通常 Release 出力は実行中の `IndigoMovieManager.exe` がロックしている場合がある。その時はユーザー実行中として扱い、プロセスを止めずに一時出力先で検証する。
- active skin 実機採取は完了。今後の再採取も、コピー `.wb` と `INDIGO_DIAGNOSTIC_NO_PERSIST=1` / `INDIGO_DIAGNOSTIC_STARTUP_DB=<コピー.wb>` だけで行う。
- same-document skip の自動確認時だけ `INDIGO_DIAGNOSTIC_REPEAT_SKIN_REFRESH=1` を併用する。通常起動やユーザーDBでは使わない。
- 次の候補は、初回 active skin navigate の `host_navigate_ms` 772.6ms 帯を、WebView2 attach / initial document / `NavigateToString` / HTML準備のどれが支配しているかに分解すること。
- さらに抜本的な UI 分割の次段は `Docs\forAI\Implementation Plan_抜本UI分割ロードマップ_2026-06-18.md` で完了監査済み。次は検索入力、sort 変更、watch reload を含む Release 実機ログが取れた時だけ、監査証跡を補強する。
