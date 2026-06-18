# Implementation Plan 長期ロードマップ 体感高速化UI分離 Worker契約 2026-06-18

> 進捗メータ: `[#######---] 71%`
> 実機ログで閉じていないものは完了扱いにしない。

## 0. 進捗メータ

更新日: 2026-06-19

全体進捗目安: `[#######---] 71%`

このメータは実装量だけではなく、focused test、Release x64 build、実機ログで説明できる度合いを含めて見る。実機ログで閉じていないものは、コードが入っていても完了扱いにしない。

| Phase | 進捗目安 | 状態 | 次に閉じること |
|---|---:|---|---|
| Phase 0. 現状固定とログ証跡補強 | 65% | `UiOperationPriorityPolicy`、ReadModel builder、partial分離、source policy は土台あり。focused test 123件で Phase 0 / 1 / 6 の入口を確認済み | 同一 Release run で search / sort / scroll / Player / watch / thumbnail / skin のログを揃える |
| Phase 1. UI Shell 入力契約 | 47% | `UiOperationSnapshot` を追加し、Everything watch / poll の実行経路も共通 snapshot を正本にした。旧 `UiOperationPrioritySnapshot` は互換入口として残す | UI event handler を snapshot 生成へさらに寄せる |
| Phase 2. ReadModel Store と Diff-first | 47% | ReadModel 計算と apply 境界は分離済み。`MovieViewDiffApplyPolicy` で query / sort / db-switch / unsafe / massive だけを full fallback 理由として固定し、ReadModel / watch の diff apply ログ fields を共通 helper へ寄せた。同一 stable key の更新、同一 key 更新に続く小さな単一連続 insert / remove、sort-only の stable key Move + Replace まで局所適用へ入った | watch 1件追加 / rename が full fallback へ戻らない実機ログと、大量変更時 fallback の妥当性を確認する |
| Phase 3. In-process Scheduler | 50% | `UiWorkRequest` / `UiWorkRequestPolicy` に加え、`UiWorkSchedulerPolicy` で bounded capacity、coalesce、latest-only、priority preempt、timeout 判定、入場ログ語彙を純粋判断として固定済み。最小 `UiWorkSchedulerRuntime` を thumbnail 進捗 snapshot refresh、Everything poll、watch reload apply 入口へ接続し、external skin host refresh queue も scheduler 語彙で読めるようにした | 実機ログで scheduler admission が操作中の割り込み抑制に効いているか確認し、必要な時だけ timeout / drain を広げる |
| Phase 4. Image Pipeline 統一 | 51% | visible range refresh と局所サムネ反映の土台に加え、上側タブ converter、詳細サムネ snapshot、Player右レール converter、サムネ進捗 preview fallback、下側 ThumbnailError 一覧 converter が `ImageRequest` を作る。`ImageLoadResult` と `ImageDecodeRequest` / `ImageDecodeResult` で、ready / missing / canceled / failed と decode 入力を同じ語彙で読める入口になり、詳細サムネの stale image request discard と ERROR一覧画像状態集約もログへ出る。Player右レール converter と ThumbnailError 一覧 converter は decode result を保持する | decode と ERROR marker 判定をさらに UI 外へ揃え、実機ログで stale discard と error tab image aggregate を確認する |
| Phase 5. Persistence Pipeline | 58% | no-persist 診断、設定保存 background queue、view_count / movie_path hot path の背景保存入口を source policy で固定済み。`PersistenceFailureNotificationPolicy` と `PersistenceWriteRequest` / `PersistenceWriteResult` により、settings / player volume / playback stats / bookmark add-delete / score / tag / movie_path / skin profile の保存ログを共通 fields で読める入口になった。application settings / player volume / playback stats / skin state の成功ログも共通語彙へ寄せた | 実機ログで保存成功 / 失敗時の dirty / failed / retryable と UI 通知候補を確認する |
| Phase 6. Worker 契約 | 52% | `ThumbnailIpcDtos` に `WorkerJobRequestDto` / `WorkerJobResultDto` / `WorkerJobProgressDto` / `WorkerJobArtifactDto` を追加し、rescue worker job JSON、thumbnail queue `QueueRequest` / 実行結果 / 進捗、watch metadata probe 入出力 / 進捗から Worker DTO へ写す adapter と focused test を追加済み。thumbnail queue、rescue worker、watch metadata probe の既存結果ログへ Worker DTO fields を併記し始めた | 実機ログで Worker DTO fields が UI 詰まりの支配要因確認に足りるかを見て、必要最小限で接続範囲を広げる |
| Phase 7. Skin / Player / Watcher の Core 接続 | 25% | skin / Player / Watcher それぞれに分離済み判断とログがあり、Watcher change set を `WatchUiApplyRequest` へ畳んで UI apply 境界を1箇所に寄せた。Player surface 操作へ保存処理を戻さない source policy も追加済み。skin host refresh queue は挙動を変えず scheduler 語彙へ接続した | skin / Player / Watcher の実行入口を Scheduler / ReadModel / Persistence 経由へ段階移行する |

## 1. Summary

- 終点は、WPF一覧を維持しながら内部を `UI Shell` / `ReadModel` / `Scheduler` / `Image Pipeline` / `Persistence Pipeline` / `Worker契約` へ分けること。
- WebView2一覧化、`.wb`変更、MainWindow全面置換、IPC / sidecar 先行導入は本線へ入れない。
- Worker / sidecar は実装先行にしない。まず thumbnail / rescue / metadata probe が UI を知らない request / result / progress / artifact 契約へ寄るところまでを長期ロードマップに含める。
- この文書は長期判断の実装順を固定する正本であり、各フェーズは小さな実装計画へ分けて進める。

## 2. 現在位置

- `UiOperationPriorityPolicy` により、検索、sort、scroll、Player 操作中に watch / poll が割り込まないための最小境界は入った。
- `UiOperationSnapshot` を追加し、search / sort / player / viewport / manual reload / watch suppression / playback を共通の軽量 snapshot として policy test で固定した。
- Everything watch / poll の実行経路は `UiOperationSnapshot` を直接作るように寄せ、旧 snapshot 名へ戻さない source policy を追加した。
- Worker契約候補は `WorkerContractSourcePolicyTests` で WPF / Dispatcher / ViewModel / WebView2 / MainWindow を参照しない source policy を追加した。
- thumbnail 進捗 refresh 予約は、coalesce / latest-only / shutdown guard を source policy で固定し、Scheduler 化の最初の足場にした。
- `UiWorkRequest` を thumbnail 進捗 refresh 予約へ接続し、priority / coalesce / latest-only / log reason / shutdown受理可否を既存経路のまま説明できるようにした。
- Everything poll は `UiWorkRequest` の `log_reason=watch.everything-poll` を作り、poll / watch defer 系ログを Scheduler 語彙へ寄せ始めた。
- watch reload 予約は `WatchUiApplyRequest` 内に `UiWorkRequest` を持たせ、query-only / full fallback の優先度、coalesce、latest-only、operation reason をログで読める入口へ寄せた。
- `BuildRequestSchedulerLogFields(...)` を追加し、thumbnail / Everything poll / watch reload の既存予約ログへ `work_priority`、`coalesce_key`、`latest_only_key`、`timeout_policy`、`bounded_drain`、`release_reason` を同じ形式で出すようにした。巨大 scheduler 本体はまだ作らない。
- `BuildRequestAdmissionLogFields(...)` を追加し、thumbnail progress / Everything poll / watch reload の既存予約ログへ `admission_action`、`admission_reason`、`queue_capacity` を同じ形式で出すようにした。bounded queue 実体はまだ作らない。
- watch 遅延 reload の cancel ログは、pending がある時だけ `release_reason=canceled` と `bounded_drain=deferred-request-cts` を出し、latest-only で古い要求を落とした証跡を読めるようにした。
- 2026-06-19 Worker D: external skin host refresh queue は scheduler 本体へ載せ替えず、queued / rejected / deferred ログへ `UiWorkRequest` の priority、coalesce、latest-only、release reason、admission fields を併記した。
- `UiWorkSchedulerPolicy` を追加し、bounded capacity、latest-only 置換、coalesce 畳み込み、満杯時の priority preempt、次実行選択、timeout release 判定を pure test で固定した。まだ `MainWindow` / `Dispatcher` / DB / UI event handler へは接続しない。
- `UiWorkSchedulerRuntime` を追加し、bounded queue / coalesce / latest-only / priority preempt / timeout release の最小状態管理を in-process で固定した。実行アクションや Dispatcher 接続はまだ作らない。
- 2026-06-18 Worker-K: `UiWorkSchedulerRuntime` を thumbnail 進捗 snapshot refresh の Queue / TryTakeNext 実経路へ最小接続した。Dispatcher / coalesce / latest-only の既存挙動は維持し、source policy で固定した。
- 2026-06-18 Worker-M: Everything poll の `QueueCheckFolderAsync(CheckMode.Watch, "EverythingPoll")` 直前を `UiWorkSchedulerRuntime` admission へ最小接続した。poll 間隔、defer / catch-up 判定、watch scan 入口は変えない。
- 2026-06-18 Worker-O: watch reload apply 入口も `WatchUiApplyRequest.WorkRequest` を使って `UiWorkSchedulerRuntime` admission へ接続した。`InvokeFilterAndSortForWatch(...)` / `RefreshMovieViewFromCurrentSourceAsync(...)` の既存分岐は維持する。
- `MovieViewDiffApplyPolicy` を追加し、query / sort / db-switch / unsafe / massive だけを full fallback 理由として判定する。`changed-path`、thumbnail 成功、単発更新のような小変更札は `none` へ畳み、既存 `ReplaceFilteredMovieRecs(...)` 互換のまま `diff_apply_kind` / `diff_apply_candidate` / `diff_full_fallback_reason` を apply log で読める入口にした。
- ReadModel / watch の diff apply ログ fields は `MovieViewDiffApplyPolicy` の helper へ寄せた。ログ語彙を1箇所にし、次段の diff-first 実適用と実機ログ比較を崩れにくくする。
- `ReplaceFilteredMovieRecs(...)` の同一 `Movie_Path` 別インスタンス更新は、remove / insert ではなく in-place replace 通知へ寄せた。単件更新のスクロール / 選択揺れを減らす diff-first の最初の実経路。
- 2026-06-19 Worker A: 同一 stable key 更新に続く小さな単一連続 insert / remove は、重複 key と reorder を避けた上で Replace + Add / Remove の局所適用へ進めた。
- 2026-06-19 Worker C: sort-only は一意な `Movie_Path` stable key の同一集合なら Move 中心で並び替え、Move 後に別インスタンスだけ Replace する経路へ広げた。
- `WatchUiApplyRequest` は `ChangedMovieCount` と `MovieViewDiffApplyPlan` を持ち、query-only change set は diff apply 候補、full fallback は full fallback として読めるようになった。実 diff apply はまだ有効化しない。
- watch UI apply request ログにも `diff_apply_kind` / `diff_apply_candidate` / `diff_full_fallback_reason` を出し、Watch query-only と ReadModel apply の差分語彙を突き合わせられるようにした。
- 画像 hot path は、詳細サムネ、Player右レール、上側タブ viewport 更新入口で file I/O / decode へ進まないことを source policy で固定した。
- 上側タブ画像 converter は `ImageRequest` を作ってから decode へ進む形へ寄せ、visible-first と stale discard を test で説明できるようにした。
- 詳細サムネ snapshot は `ImageRequest` を持ち、UI apply 直前に visible-first と request revision stale discard を通す入口を追加した。
- 詳細サムネ背景確認は `ImageProbeRequest` / `ImageProbeResult` を持ち、missing / ERROR marker / stamp 判定を UI apply ではなく背景 probe の結果としてログで読める入口へ寄せた。
- 詳細サムネ背景確認は `ImageLoadResult` も持ち、ready / missing / canceled / failed と stale skip を同じ `debug-runtime.log` で読める入口へ寄せた。
- converter 同期 decode の挙動は変えず、`ImageDecodeRequest` / `ImageDecodeResult` を追加した。上側タブ、Player右レール、サムネ進捗 preview、ThumbnailError 一覧は decode 前に同じ軽量語彙を作れる。
- Player右レール画像 converter は `ImageRequest` の `PlayerRightRail` role を作り、非表示だけでは捨てず request revision 不一致だけを stale discard する入口へ寄せた。
- 2026-06-19 Worker H: Player右レール converter は `ConvertImageRequest(...)` 丸投げではなく、`ImageDecodeRequest` から `ImageDecodeResult` を受ける形へ寄せ、stale revision skip は `ImageLoadResult.Canceled(..., "stale-player-right-rail")` として保持できる。
- サムネ進捗 preview の file fallback は `ImageRequest` の `ThumbnailProgressPreview` role を作ってから decode へ進み、メモリ優先のまま下側進捗UIも画像契約語彙で読める入口へ寄せた。
- 下側 ThumbnailError / ERROR 一覧は、背景集計で preview パスと revision を表示モデルへ持たせ、`ThumbnailErrorList` role の `ImageRequest` を作ってから converter decode へ進む入口に寄せた。UI event handler へ画像存在確認、ERROR marker 判定、decode を戻さない source policy も追加済み。
- 2026-06-19 Worker K: ThumbnailError 一覧 converter は `ConvertImageRequest(...)` 丸投げではなく、`ImageDecodeRequest` から `ImageDecodeResult` を受ける形へ寄せた。同期 decode の挙動、placeholder、missing、ERROR marker、stale 判定の意味は変えていない。
- 詳細サムネの UI apply 直前で stale image request を捨てる時も、`ImageLoadResult.Canceled(..., "stale-image-request")` と `ImageLoadLogFields` で実機ログへ残す。
- ThumbnailError / ERROR 一覧の背景集計後に、`ImageLoadResult` / `ImageLoadLogFields` 語彙の画像状態集約ログを1回だけ出す。個別行ごとの decode ログは増やさない。
- 2026-06-19 Worker B: ERROR 一覧の画像状態集約は、パスあり即 ready ではなく、背景側の存在確認、placeholder、ERROR marker、missing、failed を反映する形へ寄せた。converter の同期 decode 挙動は変えない。
- 保存 hot path は、UI操作中に同期 `Save()` や score / tag の直接DB更新へ戻らないことを source policy で固定した。
- view_count と movie_path は UI 表示値を先に反映し、DB 保存を背景へ送ることを source policy で固定した。
- skin profile write は UI hot path を enqueue のみに保ったまま、queue / persister / fallback 失敗時だけ cache と `skin-db` ログへ `dirty=true failed=true retryable=true` を出す入口を追加した。
- bookmark add / delete / view_count は既存背景経路を維持し、DB write 失敗時だけ軽量状態と `bookmark persist failed` ログへ `dirty=true failed=true retryable=true` を出す入口を追加した。
- `PersistenceFailureNotificationPolicy` を追加し、settings / score / tag / view_count / movie_path / bookmark / skin profile の保存失敗を `dirty` / `failed` / `retryable` / `notify_ui` の共通語彙へ寄せた。profile / bookmark / DB値系は retryable dirty として log-only、system 系の非 retryable 失敗だけ UI 通知候補として判定できる。同期 `Save()` や DB write は UI hot path へ戻していない。
- `PersistenceWriteRequest` / `PersistenceWriteResult` を追加し、application settings、player volume、playback stats、bookmark add / delete、score、tag、movie_path、skin profile の保存ログへ `write_kind` / `write_reason` / `queue_key` / `write_succeeded` / `failure_kind` を出せるようにした。保存実行順や hot path は変えない。
- 2026-06-19 Worker F: application settings / player volume の保存成功時も `PersistenceWriteResult.FromSuccess(...)` の共通 fields を1行だけ出し、失敗 / no-persist / background save 順序は変えていない。
- 2026-06-19 Worker I: playback stats 保存成功時も `PersistenceWriteResult.FromSuccess(...)` の共通 fields を1行だけ出し、DB更新順、`Task.Run`、例外処理、Player surface の hot path は変えていない。
- 2026-06-19 Worker L: skin state persister の system / profile 成功時も `BuildWriteSuccessResultLogFields(...)` の共通 fields を出し、batch、dedupe、DB書き込み順、cache persisted / fault の意味は変えていない。
- Worker DTO は request / result / progress / artifact の語彙を `ThumbnailIpcDtos` に追加し、JSON roundtrip と null なし既定値を focused test で固定した。
- rescue worker job JSON は `WorkerJobRequestDto` / `WorkerJobResultDto` へ写す adapter を持ち、既存 worker 実行を壊さず契約語彙へ寄せる入口ができた。
- 2026-06-19 Worker G: rescue worker の launch / result / missing result ログにも `WorkerJobRequestDto` / `WorkerJobResultDto` 由来の job id、kind、artifact、retryability、elapsed、output artifact fields を併記した。JSON schema、process launch、failfast は変えない。
- thumbnail queue の `QueueRequest` は `ThumbnailQueueWorkerContractAdapter` で `WorkerJobRequestDto` へ写せるようになり、queue runtime 側も UI 非依存の worker request 語彙で説明できる入口ができた。
- thumbnail queue の実行結果は、runtime 挙動を変えずに `WorkerJobResultDto` へ artifact path / failure kind / elapsed / retryability / metrics を写せる入口を追加した。
- 2026-06-19 Worker E: thumbnail queue の既存 `consumer done skipped` / `consumer status skipped` / `consumer failed` ログへ `WorkerJobResultDto` 由来の job id、kind、status、artifact、retryability、elapsed、failure reason fields を併記した。通常成功ごとの大量ログは増やさない。
- thumbnail queue の進捗は、runtime 挙動を変えずに `QueueDbLeaseItem` と `ThumbnailProgressRuntimeSnapshot` の軽量値から `WorkerJobProgressDto` へ写せる入口を追加した。
- watch の metadata probe は `WatchMetadataProbeWorkerContractAdapter` で `WorkerJobRequestDto` / `WorkerJobResultDto` へ写せるようになり、既存 probe 実行順や DB 更新を変えずに request / result / artifact / metrics 語彙へ寄せる入口ができた。
- watch の metadata probe 進捗も `WorkerJobProgressDto` へ写せる入口を追加し、job id / stage / current input / metrics を UI 非依存の契約語彙で保持できるようにした。
- 2026-06-19 Worker J: watch metadata probe の既存 slow / skip / refresh ログへ `worker_job_id`、`worker_kind`、`worker_status`、`worker_stage`、`artifact_kind`、`retryable`、`elapsed_ms` を併記し、probe 実行順や DB 更新は変えていない。
- Watcher change set は `WatchUiApplyRequest` へ畳んでから full fallback / in-memory ReadModel 再計算へ流し、Watcher 側が表示 collection を直接 apply しない禁止線を source policy で固定した。
- Player surface 操作は、`Properties.Settings.Default.Save()`、DB write、設定保存 queue を直接呼ばない禁止線を source policy で固定した。surface と保存の分離を壊さず、既存の user-priority と保存方針を維持する。
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
- 2026-06-19 時点では、`MovieViewDiffApplyPolicy` による判定語彙固定に加え、同一 stable key update、小さな insert / remove、sort-only stable key Move + Replace の実 apply を局所適用へ進めた。大量変更や unsafe dirty はまだ保守的に扱い、実機ログを見て次段で進める。
- `MainVM.ReplaceFilteredMovieRecs(...)` は段階的に diff apply へ置き換えるが、互換 fallback と source policy を残す。

### Phase 3. In-process Scheduler

- bounded queue、coalesce、latest-only、priority、release reason、timeout log、shutdown bounded drain を持つ小さな in-process scheduler を導入する。
- 優先順は、入力、選択、スクロール、Player、visible画像、最新検索 / sort、watch小差分、thumbnail / rescue、skin catalog の順にする。
- 最初は watch / poll / thumbnail refresh 予約だけを載せ、全機能を一気に移さない。
- 現在は実行器本体へ進む前段として、既存予約のログ語彙を `UiWorkRequestPolicy` の helper へ寄せ、さらに `UiWorkSchedulerPolicy` で入場・置換・timeout の純粋判断を固定した段階。`timeout_policy=none` は明示的な timeout drain 未導入を示し、実機ログで必要性が見えた時だけ timeout budget を持たせる。
- external skin host refresh queue も scheduler 語彙へ接続済み。ただし interval、freshness、Header Reload / fallback retry の意味は変えていない。

### Phase 4. Image Pipeline 統一

- 上側タブ、下側 ERROR / 進捗、詳細、Player右レールの画像要求を visible-first に寄せる。
- 画像存在確認、stamp取得、decode、ERROR marker判定は UI スレッドから外す。
- cache miss は placeholder で先に返し、成功 / missing / canceled / failed を revision 付きで戻す。
- 現在は下側 ThumbnailError 一覧の preview fallback と画像状態集約を `ImageRequest` / `ImageLoadResult` 語彙へ寄せた段階。次は decode と ERROR marker 判定をさらに UI 外へ揃え、実機ログで stale discard と aggregate を確認する。
- Player右レールは同期decodeのまま、`ImageDecodeResult` と stale canceled 語彙を保持する。次は実機ログで stale discard の発生有無を確認する。

### Phase 5. Persistence Pipeline

- 設定保存、score、view_count、tag、bookmark、skin profile、movie_path 更新を同じ背景保存方針へ寄せる。
- UI は表示値を先に反映し、保存は直列 background queue へ送る。
- 失敗時だけ dirty / failed / retryable をログと最小UI通知で扱う。
- application settings / player volume は保存成功時も共通 `PersistenceWriteResult` 語彙で完了を読める。実機ログで成功ログ量が多すぎる場合だけ、理由別に絞る。

### Phase 6. Worker 契約

- thumbnail / rescue / metadata probe を、UI非依存の request / result / progress / artifact 契約へ揃える。
- 既存の `ThumbnailIpcDtos`、rescue worker job json、thumbnail queue runtime を土台に、まず in-process adapter で契約を固定する。
- 済: watch metadata probe は `WatchMetadataProbeWorkerContractAdapter` で request / result / artifact / metrics へ写せる入口を追加した。runtime 挙動変更や IPC 接続はまだ行わない。
- 済: thumbnail queue の結果ログは一部 `WorkerJobResultDto` fields を併記する。成功全件ログ化はせず、既存の結果系ログだけに留める。
- 済: rescue worker の job/result JSON 経路も既存ログへ Worker DTO fields を併記する。schema変更や IPC 導入は行わない。
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
  - stable key、source revision、view revision、operation、selection impact、scroll impact、fallback reason、diff apply 候補判定を持つ。
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
  - ThumbnailError / ERROR 一覧の画像存在確認、ERROR marker 判定、decode は UI event handler へ戻さない。
  - Worker contract が WPF / Dispatcher / ViewModel を参照しない。
  - `UiWorkRequest` を使う既存予約ログは、release reason、bounded drain、coalesce、latest-only、timeout policy を共通 helper 経由で出す。
- Unit tests
  - UI operation snapshot と priority 判定。
  - ReadModel diff の apply / fallback 判定。
  - Scheduler の bounded capacity / coalesce / latest-only / priority preempt / timeout / shutdown drain。
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
