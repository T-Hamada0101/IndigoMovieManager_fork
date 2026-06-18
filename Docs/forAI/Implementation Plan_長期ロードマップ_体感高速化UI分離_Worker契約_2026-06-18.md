# Implementation Plan 長期ロードマップ 体感高速化UI分離 Worker契約 2026-06-18

> 進捗メータ: `[####------] 45%`
> 実機ログで閉じていないものは完了扱いにしない。

## 0. 進捗メータ

更新日: 2026-06-18

全体進捗目安: `[####------] 45%`

このメータは実装量だけではなく、focused test、Release x64 build、実機ログで説明できる度合いを含めて見る。実機ログで閉じていないものは、コードが入っていても完了扱いにしない。

| Phase | 進捗目安 | 状態 | 次に閉じること |
|---|---:|---|---|
| Phase 0. 現状固定とログ証跡補強 | 65% | `UiOperationPriorityPolicy`、ReadModel builder、partial分離、source policy は土台あり。focused test 123件で Phase 0 / 1 / 6 の入口を確認済み | 同一 Release run で search / sort / scroll / Player / watch / thumbnail / skin のログを揃える |
| Phase 1. UI Shell 入力契約 | 45% | `UiOperationSnapshot` を追加し、旧 `UiOperationPrioritySnapshot` から段階移行できる入口を固定済み | UI event handler を snapshot 生成へさらに寄せる |
| Phase 2. ReadModel Store と Diff-first | 25% | ReadModel 計算と apply 境界は分離済み。`MovieViewDiff` で apply log の operation / selection / scroll / fallback 語彙を追加済み | 小変更の diff apply と fallback reason を通常経路へ入れる |
| Phase 3. In-process Scheduler | 24% | `UiWorkRequest` / `UiWorkRequestPolicy` を追加し、thumbnail 進捗 refresh 予約、Everything poll、watch reload 予約へ priority / coalesce / latest-only / log reason の語彙を接続済み。shutdown受理可否は thumbnail refresh 入口で固定済み | 実行器本体を作る前に watch / poll / thumbnail の release reason と bounded drain の証跡を揃える |
| Phase 4. Image Pipeline 統一 | 32% | visible range refresh と局所サムネ反映の土台に加え、上側タブ converter、詳細サムネ snapshot、Player右レール converter が `ImageRequest` を作り、visible-first / stale discard / role / cache / revision の語彙で画像入口を説明できる | stamp取得、decode、ERROR marker 判定を UI 外へ揃える |
| Phase 5. Persistence Pipeline | 33% | no-persist 診断、設定保存 background queue、score / tag / view_count / movie_path hot path の背景保存入口を source policy で固定済み。skin profile と bookmark は失敗時に cache / log / 軽量状態で dirty / failed / retryable を読める入口を追加済み | skin profile / bookmark の最小 UI 通知条件を揃え、実機ログで失敗時だけ表現されることを確認する |
| Phase 6. Worker 契約 | 37% | `ThumbnailIpcDtos` に `WorkerJobRequestDto` / `WorkerJobResultDto` / `WorkerJobProgressDto` / `WorkerJobArtifactDto` を追加し、rescue worker job JSON と thumbnail queue `QueueRequest` から Worker DTO へ写す adapter と focused test を追加済み | thumbnail 実処理の実行結果と metadata probe を in-process adapter でこの契約へ寄せる |
| Phase 7. Skin / Player / Watcher の Core 接続 | 22% | skin / Player / Watcher それぞれに分離済み判断とログがあり、Watcher change set を `WatchUiApplyRequest` へ畳んで UI apply 境界を1箇所に寄せた | skin / Player / Watcher の実行入口を Scheduler / ReadModel / Persistence 経由へ段階移行する |

## 1. Summary

- 終点は、WPF一覧を維持しながら内部を `UI Shell` / `ReadModel` / `Scheduler` / `Image Pipeline` / `Persistence Pipeline` / `Worker契約` へ分けること。
- WebView2一覧化、`.wb`変更、MainWindow全面置換、IPC / sidecar 先行導入は本線へ入れない。
- Worker / sidecar は実装先行にしない。まず thumbnail / rescue / metadata probe が UI を知らない request / result / progress / artifact 契約へ寄るところまでを長期ロードマップに含める。
- この文書は長期判断の実装順を固定する正本であり、各フェーズは小さな実装計画へ分けて進める。

## 2. 現在位置

- `UiOperationPriorityPolicy` により、検索、sort、scroll、Player 操作中に watch / poll が割り込まないための最小境界は入った。
- `UiOperationSnapshot` を追加し、search / sort / player / viewport / manual reload / watch suppression / playback を共通の軽量 snapshot として policy test で固定した。
- Worker契約候補は `WorkerContractSourcePolicyTests` で WPF / Dispatcher / ViewModel / WebView2 / MainWindow を参照しない source policy を追加した。
- thumbnail 進捗 refresh 予約は、coalesce / latest-only / shutdown guard を source policy で固定し、Scheduler 化の最初の足場にした。
- `UiWorkRequest` を thumbnail 進捗 refresh 予約へ接続し、priority / coalesce / latest-only / log reason / shutdown受理可否を既存経路のまま説明できるようにした。
- Everything poll は `UiWorkRequest` の `log_reason=watch.everything-poll` を作り、poll / watch defer 系ログを Scheduler 語彙へ寄せ始めた。
- watch reload 予約は `WatchUiApplyRequest` 内に `UiWorkRequest` を持たせ、query-only / full fallback の優先度、coalesce、latest-only、operation reason をログで読める入口へ寄せた。
- 画像 hot path は、詳細サムネ、Player右レール、上側タブ viewport 更新入口で file I/O / decode へ進まないことを source policy で固定した。
- 上側タブ画像 converter は `ImageRequest` を作ってから decode へ進む形へ寄せ、visible-first と stale discard を test で説明できるようにした。
- 詳細サムネ snapshot は `ImageRequest` を持ち、UI apply 直前に visible-first と request revision stale discard を通す入口を追加した。
- Player右レール画像 converter は `ImageRequest` の `PlayerRightRail` role を作り、非表示だけでは捨てず request revision 不一致だけを stale discard する入口へ寄せた。
- 保存 hot path は、UI操作中に同期 `Save()` や score / tag の直接DB更新へ戻らないことを source policy で固定した。
- view_count と movie_path は UI 表示値を先に反映し、DB 保存を背景へ送ることを source policy で固定した。
- skin profile write は UI hot path を enqueue のみに保ったまま、queue / persister / fallback 失敗時だけ cache と `skin-db` ログへ `dirty=true failed=true retryable=true` を出す入口を追加した。
- bookmark add / delete / view_count は既存背景経路を維持し、DB write 失敗時だけ軽量状態と `bookmark persist failed` ログへ `dirty=true failed=true retryable=true` を出す入口を追加した。
- Worker DTO は request / result / progress / artifact の語彙を `ThumbnailIpcDtos` に追加し、JSON roundtrip と null なし既定値を focused test で固定した。
- rescue worker job JSON は `WorkerJobRequestDto` / `WorkerJobResultDto` へ写す adapter を持ち、既存 worker 実行を壊さず契約語彙へ寄せる入口ができた。
- thumbnail queue の `QueueRequest` は `ThumbnailQueueWorkerContractAdapter` で `WorkerJobRequestDto` へ写せるようになり、queue runtime 側も UI 非依存の worker request 語彙で説明できる入口ができた。
- Watcher change set は `WatchUiApplyRequest` へ畳んでから full fallback / in-memory ReadModel 再計算へ流し、Watcher 側が表示 collection を直接 apply しない禁止線を source policy で固定した。
- ReadModel 計算、一覧 apply、要求制御、表示レコード生成、MainDB runtime、起動 / dock layout / lifecycle、入力 routing は partial / helper 分離済み。
- `FilterAndSort(..., true)` は起動 fallback と段階ロード中 sort の2箇所、直書き `Refresh();` は startup first page と選択変化互換 helper の2箇所だけに固定されている。
- 次の段階は、新しい巨大 core を作ることではなく、既存境界の上に小さな契約を積み、実機ログで支配要因を確認しながら差し替えること。

## 3. Roadmap

### Phase 0. 現状固定とログ証跡補強

- 既存の `UiOperationPriorityPolicy`、ReadModel builder、partial分離、source policy を正本として固定する。
- 検索、sort、scroll、Player、watch、thumbnail、skin を同じ Release run で操作し、`debug-runtime.log` だけで割り込み、延期、fallback、apply 時間を説明できるようにする。
- `Refresh()` / `Items.Refresh()` / `FilterAndSort(..., true)` の許容線は増やさない。

### Phase 1. UI Shell 入力契約

- UI event handler は、入力、選択、スクロール、表示範囲、操作優先度の snapshot を作るだけへ寄せる。
- `UiOperationPriorityPolicy` を起点に、search / sort / player / viewport / manual reload の操作状態を共通 snapshot として扱う。
- Scheduler本体はまだ作らず、既存の user-priority / watch suppression / Everything poll delay の判断口を揃える。

### Phase 2. ReadModel Store と Diff-first

- `MovieRecords` の互換表現と UI表示用 ReadModel を段階的に分ける。
- 小変更は追加、削除、更新、移動の diff apply を通常経路にする。
- query変更、sort key変更、DB切替、大量変更、unsafe dirty だけ full fallback とし、`fallback_reason` をログへ残す。
- `MainVM.ReplaceFilteredMovieRecs(...)` は段階的に diff apply へ置き換えるが、互換 fallback と source policy を残す。

### Phase 3. In-process Scheduler

- bounded queue、coalesce、latest-only、priority、release reason、timeout log、shutdown bounded drain を持つ小さな in-process scheduler を導入する。
- 優先順は、入力、選択、スクロール、Player、visible画像、最新検索 / sort、watch小差分、thumbnail / rescue、skin catalog の順にする。
- 最初は watch / poll / thumbnail refresh 予約だけを載せ、全機能を一気に移さない。

### Phase 4. Image Pipeline 統一

- 上側タブ、下側 ERROR / 進捗、詳細、Player右レールの画像要求を visible-first に寄せる。
- 画像存在確認、stamp取得、decode、ERROR marker判定は UI スレッドから外す。
- cache miss は placeholder で先に返し、成功 / missing / canceled / failed を revision 付きで戻す。

### Phase 5. Persistence Pipeline

- 設定保存、score、view_count、tag、bookmark、skin profile、movie_path 更新を同じ背景保存方針へ寄せる。
- UI は表示値を先に反映し、保存は直列 background queue へ送る。
- 失敗時だけ dirty / failed / retryable をログと最小UI通知で扱う。

### Phase 6. Worker 契約

- thumbnail / rescue / metadata probe を、UI非依存の request / result / progress / artifact 契約へ揃える。
- 既存の `ThumbnailIpcDtos`、rescue worker job json、thumbnail queue runtime を土台に、まず in-process adapter で契約を固定する。
- sidecar / IPC は、契約が固定され、実機ログで UI 詰まりの支配要因が worker 境界にあると確認できた後だけ導入する。

### Phase 7. Skin / Player / Watcher の Core 接続

- skin refresh、Player操作、Watcher change set は UI へ直接押し込まず、Scheduler / ReadModel / Persistence 経由へ寄せる。
- skin は catalog / persist / navigate / stale を分ける。
- Player は surface操作と保存を分ける。
- Watcher は change set 正規化に専念し、UI apply の実行者にしない。

## 4. Key Interfaces

- `UiOperationSnapshot`
  - 入力中、sort中、Player操作中、recent viewport、manual reload、watch suppression、再生中を持つ軽量状態。
- `MovieViewDiff`
  - stable key、source revision、view revision、operation、selection impact、scroll impact、fallback reason を持つ。
- `UiWorkRequest`
  - priority、coalesce key、latest-only key、cancel token、timeout、log reason を持つ。
- `ImageRequest`
  - movie key、thumbnail role、visible priority、cache policy、request revision を持つ。
- `WorkerJobRequest`
  - job id、kind、input files、output artifact path、timeout、capabilities、diagnostic context を持つ。
- `WorkerJobResult`
  - status、artifact、failure reason、elapsed ms、retryability、logs、metrics を持つ。

これらは一度に作らない。各フェーズで必要になった最小型だけを追加し、使われない先行抽象は作らない。

## 5. Test Plan

- Source policy
  - `Refresh()` / `Items.Refresh()` / `FilterAndSort(..., true)` の許容線を固定する。
  - UI event handler に DB read / file I/O / image decode を戻さない。
  - Worker contract が WPF / Dispatcher / ViewModel を参照しない。
- Unit tests
  - UI operation snapshot と priority 判定。
  - ReadModel diff の apply / fallback 判定。
  - Scheduler の coalesce / latest-only / timeout / shutdown drain。
  - Image request の visible-first と stale discard。
  - Worker request / result / progress DTO の互換性。
- Focused / integration tests
  - 検索、sort、scroll、Player中に watch / poll / thumbnail が割り込まない。
  - watch 1件追加、rename、thumbnail成功が通常経路で full reload へ戻らない。
  - Release x64 build。
  - `git diff --check`。
  - 更新Doc UTF-8 BOMなし + LF。

## 6. Phase Gate

- 各フェーズは、focused test、Release x64 build、`git diff --check`、更新Docの UTF-8 BOMなし + LF を満たすまで完了扱いにしない。
- 実機ログが不足する場合は、コードだけで完了扱いにせず、残確認として明記する。
- 通常 Release 出力が実行中プロセスでロックされている時は、プロセスを止めず `.codex_build` の一時出力で検証する。

## 7. Assumptions

- WPF一覧を本線として維持する。
- `.wb` は変更しない。
- WebView2一覧化は将来検証候補であり、このロードマップの実装本線には入れない。
- Worker / sidecar は契約整備まで含めるが、IPC導入はログで必要性が確定してからにする。
- 未コミット差分は戻さない。
- 実装は小フェーズ単位で行い、各フェーズでテスト、Release build、実機ログ確認を閉じる。
