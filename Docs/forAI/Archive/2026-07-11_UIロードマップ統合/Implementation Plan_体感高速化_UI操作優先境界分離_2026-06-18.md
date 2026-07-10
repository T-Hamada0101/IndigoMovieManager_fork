# Implementation Plan 体感高速化 UI操作優先境界分離 2026-06-18

## 1. Summary

- 目的は、検索、sort、スクロール、Player 操作中に watcher / Everything poll / reload 系の後段処理が UI 操作を押しのけない状態を、既存ログで説明できるようにすること。
- WPF 一覧、既存 `BeginUserPriorityWork(...)` / `EndUserPriorityWork(...)`、watch UI suppression、Everything poll delay を維持し、巨大 scheduler / DI / sidecar は作らない。
- `.wb`、WebView2 一覧化、IPC、MainWindow 全面置換は対象外。

## 2. Key Changes

- `Views/Main/UiOperationPriorityPolicy.cs` を追加し、user-priority、manual mode、watch UI suppression、recent viewport、Player 再生中の軽い判断だけを WPF 非依存に集約した。
- sort combo の通常実行経路は `BeginUserPriorityWork("sort")` / `EndUserPriorityWork("sort")` の `try/finally` で包み、sort 中の watch / poll は既存 catch-up 経路へ逃がす。
- scroll / PageUp / PageDown は user-priority にせず、250ms の recent viewport interaction だけを記録する。Everything poll はこの間だけ一周見送り、catch-up は積まない。
- Everything poll の delay / queue probe 判断は UI 操作優先 policy 経由へ寄せ、`operation_reason`、`defer_reason`、`recent_viewport`、`catch_up`、`poll_delay_ms` を interval / defer ログで読めるようにする。

## 3. Tests

- `UiOperationPriorityPolicyTests` を追加し、user-priority、manual mode、recent viewport、Player 再生中の判断を固定する。
- `EverythingWatchPollPolicyTests` で recent viewport 中の poll defer、queue probe skip、calm delay 延長を固定する。
- `MainWindowFilterSortExecutionPolicyTests` で sort user-priority の `try/finally` と `FilterAndSort(..., true)` 許容線を固定する。
- `UpperTabViewportRefreshTests` で scroll / PageUp / PageDown が recent viewport だけを立て、`ScrollChanged` に user-priority を入れないことを固定する。

## 4. Assumptions

- 未コミット差分は戻さない。
- コミットしない。
- `Refresh()` / `Items.Refresh()` / `FilterAndSort(..., true)` の許容箇所は増やさない。
- recent viewport は操作中の wake-up 抑制だけに使い、watch catch-up reload は積まない。

## 5. Next Roadmap

- 次段の長期正本は `Docs\forAI\Implementation Plan_長期ロードマップ_体感高速化UI分離_Worker契約_2026-06-18.md`。
- UI操作優先境界は Phase 0 / Phase 1 の土台として扱い、以後は ReadModel / Scheduler / Image / Persistence / Worker契約へ小フェーズで進める。

## 6. Verification

- focused tests:
  - `UiOperationPriorityPolicyTests`
  - `EverythingWatchPollPolicyTests`
  - `MainWindowFilterSortExecutionPolicyTests`
  - `UpperTabViewportRefreshTests`
  - `WatchUiSuppressionPolicyTests`
  - `ManualPlayerResizeHookPolicyTests`
  - `WatchDeferredUiReloadPolicyTests`
- Release x64 build。
- `git diff --check`。
- 本ドキュメントは UTF-8 BOMなし、LF で保存する。

2026-06-18 実施結果:

- focused tests: 255 件成功。
- Release x64 build: 成功。
- `git diff --check`: 成功。
- 本ドキュメントおよび関連更新ドキュメント: UTF-8 BOMなし、LF。
- 通常 Release 出力は実行中の `IndigoMovieManager.exe` が DLL をロックしていたため、検証は `.codex_build\imm-uiop-*` の一時出力で実施し、完了後に削除した。
