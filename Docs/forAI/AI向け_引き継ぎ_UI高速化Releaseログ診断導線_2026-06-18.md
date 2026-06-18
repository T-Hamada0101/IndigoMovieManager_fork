# AI向け 引き継ぎ UI高速化 Releaseログ診断導線 2026-06-18

最終更新日: 2026-06-18

## 1. 現在地

- 作業ブランチ: `master`
- 最新コミット: `cb0fe38 UI高速化ログ契約と更新入口を固定`
- この引き継ぎ時点では未コミット差分あり。ユーザーの明示なしに commit しない。
- 目的は、Release 実機でも `debug-runtime.log` を採れる状態にし、active skin の `dbinfo-Skin` same-document skip が新形式ログで効くか確認できる診断導線を作ること。

## 2. 今回入っている主な未コミット変更

1. Release ログ入口の固定
   - `Infrastructure/DebugRuntimeLog.cs`
   - `Write(...)` / `TaskStart(...)` / `TaskEnd(...)` から `Conditional("DEBUG")` を外し、Release ビルドでも実機ログを残す。
   - `Tests/IndigoMovieManager.Tests/DebugRuntimeLogTests.cs` で Release でも呼び出しが消えない契約を固定。

2. source policy test の repo root 解決を強化
   - `WatcherCreationLogContractSourceTests.cs`
   - `ExternalSkinHeaderChromePolicyTests.cs`
   - `MainWindowFilterSortExecutionPolicyTests.cs`
   - `WatchDeferredUiReloadPolicyTests.cs`
   - caller source directory / cwd / test output 配下から repo root を探す形にし、`BaseOutputPath` を一時出力先へ逃がした時も source test が動くようにした。

3. active skin 実機採取用の no-persist 診断導線
   - `App.xaml.cs`
   - `Views/Main/MainWindow.xaml.cs`
   - `Views/Main/MainWindow.SettingsPersistence.cs`
   - `Views/Main/MainWindow.Player.cs`
   - `Views/Settings/CommonSettingsWindow.xaml.cs`
   - `INDIGO_DIAGNOSTIC_NO_PERSIST=1` または `true` の時だけ診断モードを有効化する。
   - `INDIGO_DIAGNOSTIC_STARTUP_DB=<コピー.wb>` は no-persist 時だけ起動DBとして扱う。
   - LastDoc / Recent / MainWindow / Player / Theme / SettingsWindow の `Settings.Save()` 系は skip ログへ閉じ、通常 user.config を汚さない。
   - 通常 AutoOpen は従来どおり `AutoOpen=true` と `LastDoc` 一致を要求する。診断DBだけは起動時に確定したコピーDB path を通す。

4. active skin same-document skip のログ契約強化
   - `Tests/IndigoMovieManager.Tests/ExternalSkinHeaderChromePolicyTests.cs`
   - `refresh end` の `navigate_skipped_current={(operationResult?.NavigateSkipped == true)}` と `navigate_skip_reason='{operationResult?.NavigateSkipReason ?? ""}'` を source policy で固定。
   - `WhiteBrowserSkinHostOperationResult.CreateNavigateSkipped(...)` が `succeeded: true` / `HostNavigateSkippedSameDocument` / `navigateSkipped: true` / `navigateSkipReason: reason` を持つことを固定。

5. 正本ドキュメント更新
   - `AGENTS.md`
   - `AI向け_現在の全体プラン_workthree_2026-03-20.md`
   - `Docs/forAI/Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md`
   - `WhiteBrowserSkin/Docs/Implementation Plan_skin切り替え高速化_DB保存分離先行_2026-04-13.md`
   - Release ログ入口、今回DBでの `CreateWatcher` 非支配、active skin 採取はコピーDB + no-persist 診断モードで行うことを反映。

## 3. 検証済み

関連テスト:

```powershell
dotnet test Tests\IndigoMovieManager.Tests\IndigoMovieManager.Tests.csproj -c Release -p:Platform=x64 -p:UseSharedCompilation=false --filter "FullyQualifiedName~MainDbSwitchPolicyTests|FullyQualifiedName~MainWindowSettingsPersistencePolicyTests|FullyQualifiedName~MainWindowUiIoDeferralSourceTests|FullyQualifiedName~ExternalSkinHeaderChromePolicyTests|FullyQualifiedName~WhiteBrowserSkinHostOperationResultTests|FullyQualifiedName~DebugRuntimeLogTests|FullyQualifiedName~WatcherCreationLogContractSourceTests"
```

結果:

- 成功
- 合格 70 件
- 失敗 0 件

Release ビルド:

```powershell
dotnet build IndigoMovieManager.sln --configuration Release -p:Platform=x64 -p:UseSharedCompilation=false -m:1 --no-restore
```

結果:

- 成功
- 警告 0
- エラー 0

差分チェック:

- `git diff --check` は成功。
- 追加差分からユーザー名、個人メール、機密ディレクトリ参照などの混入は検出なし。

## 4. 実機ログで分かったこと

Release 実機ログ入口の修正後、通常 Release 起動では `%LOCALAPPDATA%\IndigoMovieManager\logs\debug-runtime.log` が更新されることを確認済み。

確認済みの `_Anime - コピー.wb` 起動ログ:

- `first-page shown`: 498ms
- `input ready`: 499ms
- `heavy services started`: 2071ms
- `CreateWatcher`: `elapsed_ms=150`
- `watch_table_load_ms=3`
- `registration_ms=6`
- `attempted=5`
- `registered=5`
- `failed=0`

PM判断:

- このDBでは `CreateWatcher` は現在の支配要因ではない。
- 次の主対象は active skin の `dbinfo-Skin` same-document skip と、過去1件だけ出た manual reload deferred scan NullReference。

## 5. active skin 診断採取の途中経過

安全方針:

- ユーザーDBを直接変更しない。
- ユーザーDB上で外部 skin を有効化しない。
- コピー `.wb` を使う。
- no-persist 診断モードを使う。

実施済み:

1. `<コピー元DB>` を `%TEMP%\indigo-active-skin-diagnostic.wb` へコピー。
2. コピーDBの `system.skin` を `Search_table` へ変更。
3. `INDIGO_DIAGNOSTIC_NO_PERSIST=1` と `INDIGO_DIAGNOSTIC_STARTUP_DB=<コピーDB>` で Release exe を短時間起動。
4. no-persist 自体は効き、ログに以下が出た。
   - `settings save skipped: reason=apply-theme-normalize diagnostic_no_persist=1`
   - `player volume settings save skipped: diagnostic_no_persist=1`
5. 初回実行では active skin へ進まなかった。
   - 原因は `IsStartupAutoOpenLastDocSnapshotCurrent(...)` が `AutoOpen=true` と `LastDoc` 一致を要求していたため。
   - その後、診断DB時だけコピーDB path を通す修正を入れた。
6. 修正後の再テストと Release ビルドは成功。
7. 修正後の実機再起動は権限確認が拒否されたため未実施。
8. `%TEMP%\indigo-active-skin-diagnostic.wb` と一時ファイルは削除済み。

補足:

- この環境では既定の一時候補へ新規書き込みできないケースがあった。コピーDBや一時ファイルは `%TEMP%` を使う。

## 6. 次にやること

2026-06-18 追記:

- コピーDB + `INDIGO_DIAGNOSTIC_NO_PERSIST=1` / `INDIGO_DIAGNOSTIC_STARTUP_DB=<コピー.wb>` で active skin 起動は確認済み。
- 一時 Release 出力へ通常 x64 Release の `skin` フォルダを揃えたうえで、`VSTB` 初回 navigate は `active=True ready=True reason=dbinfo-DBFullPath host_navigate_ms=772.6 navigate_to_string_ms=171.6`。
- `INDIGO_DIAGNOSTIC_REPEAT_SKIN_REFRESH=1` を併用し、初回 active skin 成功後に同一 `dbinfo-Skin` を 1 回だけ再要求して、`errorType=HostNavigateSkippedSameDocument navigate_skipped_current=True navigate_skip_reason='same-document' navigate_to_string_ms=0.0` を確認済み。
- 診断用コピーDBと生成サムネは削除済み。ユーザーDBは直接変更していない。

1. まず `git status --short` と `git diff --stat` を確認する。
2. 既存の未コミット差分を壊さず、診断DBガード修正後の active skin 実機採取を再実行する。
3. 採取手順は次の順序にする。
   - `%TEMP%` にコピーDBを作る。
   - コピーDBの `system.skin` を `Search_table` など外部 skin に変更する。
   - `INDIGO_DIAGNOSTIC_NO_PERSIST=1`
   - `INDIGO_DIAGNOSTIC_STARTUP_DB=<コピーDB>`
   - Release exe を 20〜30 秒起動し、自分が起動した PID だけ閉じる。
   - 増えた `debug-runtime.log` 行だけを読む。
4. 見るべきログ:
   - `startup auto-open`
   - `main-db`
   - `refresh begin`
   - `refresh end active=True`
   - `host_navigate_ms`
   - `navigate_to_string_ms`
   - `navigate_skipped_current`
   - `navigate_skip_reason`
   - `diagnostic_no_persist=1`
5. active=True が出なければ、診断DB切替がどこで止まったかをログ追加または source policy で補強する。
6. active=True だが `navigate_skipped_current=True` が出なければ、同一 document 条件を満たす `dbinfo-*` refresh をコピーDB上で安全に起こす手順を詰める。
7. 実機ログで確認できたら、正本ドキュメントの進捗を更新する。
8. ユーザーが明示したら commit する。コミットメッセージ候補は `UI高速化のRelease診断導線を整備`。

## 7. 次担当向けプロンプト

```text
常に日本語で回答。愛を持って簡潔に。

あなたはPM兼レビュー担当。%USERPROFILE%\source\repos\IndigoMovieManager で作業する。
まず AGENTS.md、AI向け_現在の全体プラン_workthree_2026-03-20.md、Docs\forAI\Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md、Docs\forAI\AI向け_引き継ぎ_UI高速化Releaseログ診断導線_2026-06-18.md を読む。

現在の目的:
- Release 実機でも debug-runtime.log が出るようにした未コミット差分をレビューし、active skin の dbinfo-Skin same-document skip が新形式ログで確認できるところまで進める。
- ユーザーDBは直接変更しない。active skin 採取はコピー .wb + INDIGO_DIAGNOSTIC_NO_PERSIST=1 + INDIGO_DIAGNOSTIC_STARTUP_DB=<コピー.wb> で行う。
- ユーザーが明示するまで commit しない。

最初にやること:
1. git status --short と git diff --stat を確認。
2. 関連テストと Release ビルドが通るか必要に応じて再確認。
3. %TEMP% に <コピー元DB> のコピーを作り、コピーDBの system.skin だけ Search_table へ変更。
4. Release exe を診断環境変数付きで 20〜30 秒だけ起動し、自分で起動した PID だけ閉じる。
5. 増えた %LOCALAPPDATA%\IndigoMovieManager\logs\debug-runtime.log 行だけを解析。

見るログ:
- diagnostic_no_persist=1 の skip ログ
- startup auto-open / main-db 切替ログ
- skin-webview refresh begin/end
- refresh end active=True
- host_navigate_ms / navigate_to_string_ms
- navigate_skipped_current / navigate_skip_reason

判断:
- active=True が出なければ、診断DB切替のガードまたはログを最小修正。
- active=True だが same-document skip が出なければ、コピーDB上で安全に dbinfo-* refresh を起こす手順をサブ2名にレビューさせる。
- header-reload / fallback-notice-retry は CatalogRefresh のまま維持し、same-document skip 対象へ広げない。
- watcher は今回DBでは elapsed_ms=150 で非支配。別DBで遅いログが出た時だけ CreateWatcher 内訳を見る。

検証後:
- Docs/forAI/Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md、AI向け_現在の全体プラン_workthree_2026-03-20.md、AGENTS.md、WhiteBrowserSkin/Docs/Implementation Plan_skin切り替え高速化_DB保存分離先行_2026-04-13.md の必要箇所を更新。
- git diff --check とローカル固有情報混入チェックを行う。
- ユーザーが commit と明示した場合だけ、日本語コミットメッセージで 1目的 1コミットにする。
```

## 8. サブエージェント投入用プロンプト

### サブA: 診断採取導線レビュー

```text
読み取り専任でレビュー。対象は active skin 実機採取導線。
AGENTS.md と Docs\forAI\AI向け_引き継ぎ_UI高速化Releaseログ診断導線_2026-06-18.md を読んだ上で、INDIGO_DIAGNOSTIC_NO_PERSIST=1 / INDIGO_DIAGNOSTIC_STARTUP_DB=<コピー.wb> の導線が user.config / LastDoc / Recent / ユーザーDBを汚さず active skin を起動できるか確認して。
特に Views/Main/MainWindow.xaml.cs、App.xaml.cs、Views/Main/MainWindow.SettingsPersistence.cs、Views/Main/MainWindow.Player.cs、Views/Settings/CommonSettingsWindow.xaml.cs、Views/Main/MainWindow.DbSwitch.cs を見る。
実装変更はしない。危険な保存入口、足りないログ、テスト不足を優先度付きで報告して。
```

### サブB: active skin ログ確認手順レビュー

```text
読み取り専任でレビュー。対象は active skin の dbinfo-Skin same-document skip を実機ログで確認する手順。
Docs\forAI\AI向け_引き継ぎ_UI高速化Releaseログ診断導線_2026-06-18.md と WhiteBrowserSkin\Docs\Implementation Plan_skin切り替え高速化_DB保存分離先行_2026-04-13.md を読んで、コピーDB上で安全に active=True と navigate_skipped_current=True を出す最短手順を提案して。
header-reload / fallback-notice-retry は CatalogRefresh のまま維持し、skip 対象へ広げない前提。
実装変更はしない。必要なログ行、成功条件、失敗時の切り分け順を報告して。
```
