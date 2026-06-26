# Implementation Plan 長期ロードマップ 体感高速化UI分離 Worker契約 2026-06-18

> 進捗メータ: `[#########-] 91%`
> 実機ログで閉じていないものは完了扱いにしない。

## 0. 進捗メータ

更新日: 2026-06-27

全体進捗目安: `[#########-] 91%`

このメータは実装量だけではなく、focused test、Release x64 build、実機ログで説明できる度合いを含めて見る。実機ログで閉じていないものは、コードが入っていても完了扱いにしない。

| Phase | 進捗目安 | 状態 | 次に閉じること |
|---|---:|---|---|
| Phase 0. 現状固定とログ証跡補強 | 80% | `UiOperationPriorityPolicy`、ReadModel builder、partial分離、source policy は土台あり。さらに UI Shell / ReadModel Diff / Scheduler / Image / Persistence / Worker / Skin / Player / Watcher の contract source policy を focused test 159件で確認済み。最新run切り出し、run時間窓、run要約、contract / Phase0 evidence 集計、次アクション、監査summary合成 policy、Logタブ preview summary 実出力も focused test 30件で確認済み。search / sort 入力入口の `ui shell input` 出力固定と opt-in live audit test も追加済み。2026-06-27 は contract 完了条件と live audit source policy を固定し、実ログ audit で `log_evidence=2/9`、`phase0_log_evidence=1/12` の不足を確認済み。audit summary は `phase0_audit_complete=true|false` を出し、scroll / manual reload / ReadModel Diff detail / image aggregate / stale discard / Worker detail / Phase7 core route detail の evidence を必須と補助に分けて扱う。さらに Phase2 / Phase3 optional evidence として ReadModel revision / fallback と Scheduler detail を補強し、Refresh / Items.Refresh / `FilterAndSort(..., true)` 許容線の広域 source policy も補強した | 同一 Release run で search / sort / scroll / Player / watch / thumbnail / skin のログを揃える |
| Phase 1. UI Shell 入力契約 | 56% | `UiOperationSnapshot` を追加し、Everything watch / poll と user-priority 判定入口を共通 snapshot 正本へ寄せた。旧 `UiOperationPrioritySnapshot` は互換入口として残すが、MainWindow runtime 側の判定口では使わない。2026-06-25 Worker E で snapshot 共通ログ fields を追加し、user-priority begin / end でも UI Shell 入力状態を同じ語彙で読めるようにした。Worker Averroes で search / sort 入力入口にも `ui shell input` と snapshot fields、Worker Hilbert で `ui_shell_contract=ui-shell-v1` を追加した。2026-06-27 は Player 再生状態の実遷移、PageUp / PageDown 成功時、Header Reload 明示手動再読込にも `ui shell input` snapshot を追加した。manual reload は Phase0 必須 token を増やさず optional evidence として監査summaryへ出す | UI event handler を snapshot 生成へさらに寄せ、実機ログで `ui_shell_contract=ui-shell-v1` と search / sort / scroll / player / manual-reload の入口状態を確認する |
| Phase 2. ReadModel Store と Diff-first | 56% | ReadModel 計算と apply 境界は分離済み。`MovieViewDiffApplyPolicy` で query / sort / db-switch / unsafe / massive だけを full fallback 理由として固定し、ReadModel / watch の diff apply ログ fields を共通 helper へ寄せた。同一 stable key の更新、同一 key 更新に続く小さな単一連続 insert / remove、sort-only の stable key Move + Replace まで局所適用へ入った。さらに DB 登録済み行は `Movie_Id` を stable key の優先候補にし、path rename / movie_path 更新でも同一動画なら Replace update へ進める。2026-06-25 Worker F で watch apply request ログへ source / applied changed paths と `diff_change_set`、Worker Curie で diff ログへ `diff_changed_total`、Worker Fermat で watch apply request の change set ログにも `diff_changed_total`、Worker Chandrasekhar で `diff_contract=readmodel-diff-v1` を追加した。2026-06-27 は `diff_change_set=single` / `diff_changed_total=` を Phase0 optional evidence として audit summary へ残すようにした。さらに WPF本体C#全体で `Items.Refresh()` 復帰を検出し、直書き `Refresh();` と `FilterAndSort(..., true)` の許容2箇所を source policy で固定した | watch 1件追加 / rename が `diff_contract=readmodel-diff-v1`、`diff_change_set=single`、`diff_changed_total=1` のまま full fallback へ戻らない実機ログと、大量変更時 fallback の `diff_changed_total` の妥当性を確認する |
| Phase 3. In-process Scheduler | 60% | `UiWorkRequest` / `UiWorkRequestPolicy` に加え、`UiWorkSchedulerPolicy` で bounded capacity、coalesce、latest-only、priority preempt、timeout 判定、入場ログ語彙を純粋判断として固定済み。最小 `UiWorkSchedulerRuntime` を thumbnail 進捗 snapshot refresh、Everything poll、watch reload apply 入口へ接続し、external skin host refresh queue と kana backfill ReadModel refresh も scheduler 語彙で読めるようにした。終了時に pending が残った場合も lifecycle ログで読める。2026-06-25 Worker A で kana backfill の受理成功と既存 refresh 入口への release 証跡を補強し、Worker Lagrange で admission / take ログへ判定結果 fields、Worker Zeno で timeout ログへ `timeout_released`、Worker Locke で timeout release ログへ `sequence` / `pending_count_after`、Worker Curie(Scheduler) で `scheduler_contract=scheduler-v1` を追加した。2026-06-27 は shutdown pending summary も `scheduler_contract=scheduler-v1` を持つようにし、pending item は `BuildRequestSchedulerLogFields(...)` 経由で固定した。さらに admission / take / timeout release の詳細 fields を source policy と runtime test で補強した | 実機ログで `scheduler_contract=scheduler-v1`、scheduler admission / released / pending_count / accepted / target_index / has_request / timeout_released / pending_count_after / shutdown pending が操作中の割り込み抑制と終了時観測に効いているか確認し、必要な時だけ timeout / drain を広げる |
| Phase 4. Image Pipeline 統一 | 60% | visible range refresh と局所サムネ反映の土台に加え、上側タブ converter、詳細サムネ snapshot、Player右レール converter、サムネ進捗 preview fallback、下側 ThumbnailError 一覧 converter が `ImageRequest` を作る。`ImageLoadResult` と `ImageDecodeRequest` / `ImageDecodeResult` で、ready / missing / canceled / failed と decode 入力を同じ語彙で読める入口になり、詳細サムネの stale image request discard と ERROR一覧画像状態集約もログへ出る。上側タブ、Player右レール、ThumbnailError 一覧 converter は decode result を保持する。2026-06-25 Worker B で ThumbnailError 背景集計に `ImageDecodePlanResult`、Worker Orion で decode plan ログへ load 状態 fields、Worker Cicero で image load / decode ログへ `visible_priority` / `image_cache_policy` / `should_decode`、Worker Faraday(Image) で image load / decode / decode plan ログへ `image_key`、Worker McClintock で `image_contract=image-pipeline-v1` を追加した。2026-06-27 は aggregate-decode-plan と stale discard を Phase0 optional evidence として audit summary へ残すようにした。さらに image load / decode / aggregate-decode-plan / stale discard の詳細 fields を source policy / unit test で補強した | 実機ログで stale discard と error tab image aggregate / aggregate-decode-plan の `image_contract=image-pipeline-v1`、`image_key` / `image_result_revision` / `resolved` / `placeholder` / `stale` / `failure_reason` / visible request fields を確認する |
| Phase 5. Persistence Pipeline | 66% | no-persist 診断、設定保存 background queue、view_count / movie_path hot path の背景保存入口を source policy で固定済み。`PersistenceFailureNotificationPolicy` と `PersistenceWriteRequest` / `PersistenceWriteResult` により、settings / player volume / playback stats / bookmark add-delete / score / tag / movie_path / skin profile の保存ログを共通 fields で読める入口になった。application settings / player volume / playback stats / skin state の成功ログも共通語彙へ寄せ、score / tag / movie_path の成功ログも `PersistenceWriteRequest` helper 経由に揃えた。2026-06-25 Worker C で成功ログにも状態語彙を出し、Worker Noether で結果ログへ `persist_state`、Worker Galileo で共通 write fields へ `persist_contract=persistence-write-v1` を追加した。2026-06-27 は bookmark / score / tag / movie_path / playback stats の hot path 保存ログで `persist_state` が戻らないことをテストで補強した。さらに保存結果ログの contract / success / state fields を focused tests で補強した | 実機ログで `persist_contract=persistence-write-v1`、`write_succeeded=true/false`、`persist_state`、dirty / failed / retryable / notify_ui の組み合わせを確認する |
| Phase 6. Worker 契約 | 63% | `ThumbnailIpcDtos` に `WorkerJobRequestDto` / `WorkerJobResultDto` / `WorkerJobProgressDto` / `WorkerJobArtifactDto` を追加し、rescue worker job JSON、thumbnail queue `QueueRequest` / 実行結果 / 進捗、watch metadata probe 入出力 / 進捗から Worker DTO へ写す adapter と focused test を追加済み。thumbnail queue、rescue worker、watch metadata probe の既存結果ログへ Worker DTO fields を併記し始めた。2026-06-25 Worker D で queue の failure / skip 系ログへ request / progress / result の代表 fields をまとめて併記し、Worker Meitner で Thumbnail Queue の request / queue 代表ログへ `capability_count` と `diagnostic_context_count`、Worker Harvey で watch metadata probe の request / probe 統合ログへ `diagnostic_context_count`、Worker Faraday で rescue worker request ログへ `diagnostic_context_count`、Worker Anscombe で result 系ログへ `metric_count`、Worker Turing で `worker_contract=worker-job-v1` を追加した。2026-06-27 は `diagnostic_context_count` / `capability_count` / `metric_count` を Phase0 optional evidence として summary へ残すようにした。さらに failure / skip / result 系の status / stage / retry / failure / count 語彙を focused tests で補強し、rescue worker result ログへ `worker_stage` / `retryable` / `failure_kind` を追加した | 実機ログで `worker_contract=worker-job-v1` と Worker DTO fields、`capability_count` / `diagnostic_context_count` / `metric_count` / `worker_stage` / `retryable` / `failure_kind` が failure / skip 系ログ、probe 系ログ、rescue worker result ログの支配要因確認に足りるかを見て、必要最小限で接続範囲を広げる |
| Phase 7. Skin / Player / Watcher の Core 接続 | 39% | skin / Player / Watcher それぞれに分離済み判断とログがあり、Watcher change set を `WatchUiApplyRequest` へ畳んで UI apply 境界を1箇所に寄せた。Player surface 操作へ保存処理を戻さない source policy も追加済み。skin host refresh queue は挙動を変えず scheduler 語彙へ接続済み。2026-06-25 Worker G/H で skin / Player の core route を併記し、Worker Kant で Watcher apply request に `core_route=watch-ui-apply` / `watch_apply_kind` / `watch_reason` / `operation_reason`、Worker Gibbs で Player core route に `player_surface_ready`、Worker Leibniz / Banach で `operation_reason=skin.host-refresh` と `player_transition=start|stop` を併記した。2026-06-27 は Player 実遷移時の UI Shell input snapshot と Player core route を対で読めるようにし、skin / Player / Watcher の core route 詳細値をテストで固定した。さらに `core_route` と同じ行にある detail field だけを Phase0 optional evidence として拾うようにした | 実機ログで skin / Player / Watcher それぞれの core_route、scheduler fields、surface / apply kind / surface ready / transition を確認し、skin / Player / Watcher の実行入口を段階接続する |

## 1. Summary

- 終点は、WPF一覧を維持しながら内部を `UI Shell` / `ReadModel` / `Scheduler` / `Image Pipeline` / `Persistence Pipeline` / `Worker契約` へ分けること。
- WebView2一覧化、`.wb`変更、MainWindow全面置換、IPC / sidecar 先行導入は本線へ入れない。
- Worker / sidecar は実装先行にしない。まず thumbnail / rescue / metadata probe が UI を知らない request / result / progress / artifact 契約へ寄るところまでを長期ロードマップに含める。
- この文書は長期判断の実装順を固定する正本であり、各フェーズは小さな実装計画へ分けて進める。

## 2. 現在位置

- `UiOperationPriorityPolicy` により、検索、sort、scroll、Player 操作中に watch / poll が割り込まないための最小境界は入った。
- `UiOperationSnapshot` を追加し、search / sort / player / viewport / manual reload / watch suppression / playback を共通の軽量 snapshot として policy test で固定した。
- Everything watch / poll の実行経路は `UiOperationSnapshot` を直接作るように寄せ、旧 snapshot 名へ戻さない source policy を追加した。
- 2026-06-19 Worker M: user-priority 判定入口も `UiOperationSnapshot` を直接作る形へ寄せた。旧 `UiOperationPrioritySnapshot` は互換入口として残すが、MainWindow runtime 側の判定では使わない。
- 2026-06-25 Worker E: `UiOperationPriorityPolicy.BuildSnapshotLogFields(...)` を追加し、user-priority begin / end ログに `is_user_priority_active` / `is_manual_mode` / `is_watch_ui_suppressed` / `is_recent_viewport_active` / `is_player_playback_active` を併記した。検索 / sort / Player の実行順や catch-up 条件は変えていない。
- 2026-06-27 Worker Boyle: opt-in live audit は Phase0 操作 evidence だけでなく contract evidence の完了も必須にした。`ContractEvidence.IsComplete` と `Phase0Evidence.IsComplete` の両方が真でなければ、summary と次アクションを出して失敗する。
- 2026-06-27 Worker Lovelace: live audit の opt-in 環境変数、任意ログパス、`LOCALAPPDATA` 既定、共有読み取り、完了条件、失敗summaryを source policy で固定した。採取導線のテストだけで、runtime ログ出力や UI 操作順は変えていない。
- 2026-06-27 Worker Confucius: UI簡素化後の MainWindow 標準ヘッダーは、10列高密度配置、検索欄 / DBパス / fallback notice、ヘッダーボタン、ComboBox style を source policy で固定した。テーマの見た目確認を支えるテストであり、入力挙動は変えていない。
- 2026-06-27 Worker Arendt: audit summary は contract evidence と Phase0 evidence の両方を見た `phase0_audit_complete=true|false` を出すようにした。Logタブ preview と live audit 失敗メッセージで、完了扱いにしてよいかを1行で読める。
- 2026-06-27 Worker Dirac: Player 再生状態の実遷移時だけ `ui-priority` へ `ui shell input` snapshot を出し、同じ遷移で既存の `player core_route=player-playback` ログも維持した。同状態通知ではログを増やさない。
- 2026-06-27 Worker Mill: Phase0 の scroll evidence は新語彙 `ui shell input: operation_reason=scroll` を優先しつつ、既存採取済みログの `page scroll end:` も同じ `scroll-input` として認識する。required count は 12 のまま増やさない。
- 2026-06-27 Worker Godel: PageUp / PageDown の実スクロール成功後だけ `ui-priority` へ `ui shell input` snapshot を出す。scroll は user-priority にせず recent viewport のまま扱い、`BeginUserPriorityWork` は追加していない。
- 2026-06-27 Worker Darwin: Header Reload 明示手動再読込入口で `ui-priority` へ `ui shell input: operation_reason=manual-reload` を出す。`is_manual_mode=true` とし、既存の `header reload begin/end/failed`、watch suppression、deferred scan の順序は変えない。
- 2026-06-27 Worker Boole: manual reload 入力ログは Phase0 必須12 token に足さず、`optional=manual-reload-input` として audit summary へ出す。採取手順を重くせず、Phase1 UI Shell の補助 evidence として見る。
- 2026-06-27 Worker Copernicus: scheduler shutdown pending summary へ `scheduler_contract=scheduler-v1` を追加した。pending item 行は `BuildRequestSchedulerLogFields(...)` 経由のまま維持し、終了順序や queue 解放条件は変えない。
- 2026-06-27 Worker Pascal: shutdown pending source policy を補強し、summary 行が contract field を持つことと pending item が helper 経由で出ることを固定した。
- 2026-06-27 Worker Bacon: Phase4 画像ログの `image.thumbnail-error-list.aggregate-decode-plan` と stale discard を Phase0 optional evidence として扱う。`image-pipeline` 必須 token は `image_contract=image-pipeline-v1` のまま維持し、Phase0 必須12件は増やさない。
- 2026-06-27 Worker Hubble: bookmark / score / tag / movie_path / playback stats の hot path 保存ログで `persist_contract=persistence-write-v1` と `persist_state` が同じ結果ログに乗ることをテストで固定した。保存順、retryable 判定、dirty / failed / notify UI の意味は変えない。
- 2026-06-27 Worker Laplace: Worker DTO detail の `diagnostic_context_count` / `capability_count` / `metric_count` を Phase0 optional evidence として扱う。`worker` 必須 token は `worker_contract=worker-job-v1` のまま維持し、Phase0 必須12件は増やさない。
- 2026-06-27 Worker Singer: skin / Player / Watcher の core route 詳細値を focused tests で固定した。skin refresh / Player playback / Watch apply の実行順や判定は変えていない。
- 2026-06-27 Worker Fermat: ReadModel Diff 詳細の `diff_change_set=single` / `diff_changed_total=` を Phase0 optional evidence として扱う。`readmodel-diff` contract evidence は `diff_contract=readmodel-diff-v1` のまま維持し、Phase0 必須12件は増やさない。
- 2026-06-27 Worker Sagan: Scheduler admission / take / timeout release の詳細 fields を source policy と runtime test で固定した。queue 判定、timeout / drain 条件、UI 実行順は変えていない。
- 2026-06-27 Worker Rawls: Image load / decode / aggregate-decode-plan / stale discard の詳細 fields を focused tests で固定した。production、UI、XAML、Themes、Settings は変えていない。
- 2026-06-27 Worker Banach: Persistence 保存結果ログの contract / write_succeeded / persist_state / dirty / failed / retryable / notify_ui を focused tests で固定した。保存順、retryable 判定、dirty / failed / notify_ui の意味は変えていない。
- 2026-06-27 Worker Faraday(Phase0): Phase0 optional evidence に ReadModel Diff の source / view revision と fallback reason、Scheduler の accepted / target_index / has_request / timeout_released / pending_count_after を追加した。Phase0 必須12件は増やしていない。
- 2026-06-27 Worker Parfit: Worker契約の failure / skip / result 代表語彙を focused tests で固定し、rescue worker result ログへ `worker_stage=completed` / `retryable` / `failure_kind` を追加した。queue 実行順、worker 起動順、IPC方式は変えていない。
- 2026-06-27 PM補正: 外部skinヘッダー source policy の旧 48px / 170px / 420px 期待値を、UI簡素化後の 32px / 132px / 320px 契約へ更新した。XAML、Themes、Settings 画面は触っていない。
- 2026-06-27 Worker Schrodinger: Refresh / Items.Refresh / `FilterAndSort(..., true)` の許容線 source policy を補強した。WPF本体C#全体の `Items.Refresh()` 復帰、直書き `Refresh();` の許容2箇所、`FilterAndSort(..., true)` の許容2箇所を検出するテストであり、production UI / XAML / Themes / Settings / runtime 挙動は変えていない。
- 2026-06-27 Worker Halley: Phase7 core route detail の `skin-operation-reason` / `skin-definition-mode` / `player-surface-ready` / `player-transition` / `watch-apply-kind` / `watch-reason` を Phase0 optional evidence へ追加した。`core_route` と同じ行にある時だけ採用し、Phase0 必須12件は増やしていない。
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
- 2026-06-19 Worker N: kana backfill の ReadModel refresh 予約は、実行順を変える runtime 接続までは入れず、`UiWorkRequestPolicy.CreateKanaBackfillMovieViewRefreshRequest()` と既存 fallback ログの scheduler fields で説明できるようにした。
- 2026-06-19 PM S: MainWindow closing 時に `UiWorkSchedulerRuntime` の pending request が残っていれば lifecycle ログへ `release_reason=canceled` 語彙で出す。終了処理を延ばさず、実行順や queue 解放条件は変えない。
- 2026-06-25 Worker A: kana backfill の ReadModel refresh 予約は実行順を変えず、受理成功時も `admission` / `released` / `pending_count` をログで読めるようにした。`BuildTakeLogFields(...)` で pending から既存 refresh 入口へ渡した証跡を共通語彙へ寄せた。
- 2026-06-25 Worker Locke: timeout release ログは `UiWorkSchedulerPolicy.BuildTimeoutReleaseLogFields(...)` 経由になり、timeout で落ちた pending の `sequence` と解放後 `pending_count_after` を同じ行で読めるようにした。timeout 判定、削除順、queue 操作、戻り値の意味は変えていない。
- `MovieViewDiffApplyPolicy` を追加し、query / sort / db-switch / unsafe / massive だけを full fallback 理由として判定する。`changed-path`、thumbnail 成功、単発更新のような小変更札は `none` へ畳み、既存 `ReplaceFilteredMovieRecs(...)` 互換のまま `diff_apply_kind` / `diff_apply_candidate` / `diff_full_fallback_reason` を apply log で読める入口にした。
- ReadModel / watch の diff apply ログ fields は `MovieViewDiffApplyPolicy` の helper へ寄せた。ログ語彙を1箇所にし、次段の diff-first 実適用と実機ログ比較を崩れにくくする。
- `ReplaceFilteredMovieRecs(...)` の同一 `Movie_Path` 別インスタンス更新は、remove / insert ではなく in-place replace 通知へ寄せた。単件更新のスクロール / 選択揺れを減らす diff-first の最初の実経路。
- 2026-06-19 Worker A: 同一 stable key 更新に続く小さな単一連続 insert / remove は、重複 key と reorder を避けた上で Replace + Add / Remove の局所適用へ進めた。
- 2026-06-19 Worker C: sort-only は一意な `Movie_Path` stable key の同一集合なら Move 中心で並び替え、Move 後に別インスタンスだけ Replace する経路へ広げた。
- 2026-06-19 Worker O: 一覧 diff の stable key は DB 登録済み行で `Movie_Id` を優先し、path rename / movie_path 更新でも同一動画なら remove / insert ではなく Replace update へ進める。`Movie_Id` / `Movie_Path` の重複は従来どおり fallback に戻す。
- `WatchUiApplyRequest` は `ChangedMovieCount` と `MovieViewDiffApplyPlan` を持ち、query-only change set は diff apply 候補、full fallback は full fallback として読めるようになった。実 diff apply はまだ有効化しない。
- watch UI apply request ログにも `diff_apply_kind` / `diff_apply_candidate` / `diff_full_fallback_reason` を出し、Watch query-only と ReadModel apply の差分語彙を突き合わせられるようにした。
- 2026-06-25 Worker F: watch apply request ログへ `source_changed_paths` / `applied_changed_paths` / `diff_change_set=none|single|multiple` を追加し、full fallback でも元の change set 規模を読めるようにした。query-only / full fallback の判定と通常成功ログ量は変えていない。
- 2026-06-25 Worker Curie: `MovieViewDiffApplyPolicy.BuildDiffLogFields(...)` へ `diff_changed_total` を追加し、added / deleted / updated / moved の合計規模を ReadModel / watch の共通ログで読めるようにした。diff apply 判定、collection apply、fallback 条件、UI 挙動は変えていない。
- 2026-06-25 Worker Fermat: watch apply request の change set ログにも `diff_changed_total` を追加し、source / applied 件数、`diff_change_set`、ReadModel diff total を同じ語彙で突き合わせられるようにした。query-only / full fallback 判定、scheduler admission、UI apply の実行順は変えていない。
- 画像 hot path は、詳細サムネ、Player右レール、上側タブ viewport 更新入口で file I/O / decode へ進まないことを source policy で固定した。
- 上側タブ画像 converter は `ImageRequest` を作ってから decode へ進む形へ寄せ、visible-first と stale discard を test で説明できるようにした。
- 詳細サムネ snapshot は `ImageRequest` を持ち、UI apply 直前に visible-first と request revision stale discard を通す入口を追加した。
- 詳細サムネ背景確認は `ImageProbeRequest` / `ImageProbeResult` を持ち、missing / ERROR marker / stamp 判定を UI apply ではなく背景 probe の結果としてログで読める入口へ寄せた。
- 詳細サムネ背景確認は `ImageLoadResult` も持ち、ready / missing / canceled / failed と stale skip を同じ `debug-runtime.log` で読める入口へ寄せた。
- converter 同期 decode の挙動は変えず、`ImageDecodeRequest` / `ImageDecodeResult` を追加した。上側タブ、Player右レール、サムネ進捗 preview、ThumbnailError 一覧は decode 前に同じ軽量語彙を作れる。
- 2026-06-19 Worker Q: 上側タブ画像 converter は `ConvertImageRequest(...)` 直返しではなく、`ImageDecodeRequest` から `ImageDecodeResult` を受ける形へ寄せた。同期 decode の挙動と placeholder の意味は変えず、UpperTab role の結果だけを返す。
- Player右レール画像 converter は `ImageRequest` の `PlayerRightRail` role を作り、非表示だけでは捨てず request revision 不一致だけを stale discard する入口へ寄せた。
- 2026-06-19 Worker H: Player右レール converter は `ConvertImageRequest(...)` 丸投げではなく、`ImageDecodeRequest` から `ImageDecodeResult` を受ける形へ寄せ、stale revision skip は `ImageLoadResult.Canceled(..., "stale-player-right-rail")` として保持できる。
- サムネ進捗 preview の file fallback は `ImageRequest` の `ThumbnailProgressPreview` role を作ってから decode へ進み、メモリ優先のまま下側進捗UIも画像契約語彙で読める入口へ寄せた。
- 下側 ThumbnailError / ERROR 一覧は、背景集計で preview パスと revision を表示モデルへ持たせ、`ThumbnailErrorList` role の `ImageRequest` を作ってから converter decode へ進む入口に寄せた。UI event handler へ画像存在確認、ERROR marker 判定、decode を戻さない source policy も追加済み。
- 2026-06-19 Worker K: ThumbnailError 一覧 converter は `ConvertImageRequest(...)` 丸投げではなく、`ImageDecodeRequest` から `ImageDecodeResult` を受ける形へ寄せた。同期 decode の挙動、placeholder、missing、ERROR marker、stale 判定の意味は変えていない。
- 詳細サムネの UI apply 直前で stale image request を捨てる時も、`ImageLoadResult.Canceled(..., "stale-image-request")` と `ImageLoadLogFields` で実機ログへ残す。
- ThumbnailError / ERROR 一覧の背景集計後に、`ImageLoadResult` / `ImageLoadLogFields` 語彙の画像状態集約ログを1回だけ出す。個別行ごとの decode ログは増やさない。
- 2026-06-19 Worker B: ERROR 一覧の画像状態集約は、パスあり即 ready ではなく、背景側の存在確認、placeholder、ERROR marker、missing、failed を反映する形へ寄せた。converter の同期 decode 挙動は変えない。
- 2026-06-25 Worker B: ThumbnailError / ERROR 一覧の背景集計は `ImageDecodePlanResult` を作り、`decode_attempted=false` のまま `sample_decode` を aggregate ログへ併記する。ERROR marker / placeholder / missing 判定は UI 外の集計結果として説明できるが、converter の同期 decode と個別行ログ量は変えない。
- 2026-06-25 Worker Orion: `ImageDecodePlanLogFields.Build(...)` へ `image_result_revision` / `resolved` / `placeholder` / `stale` / `failure_reason` を追加し、aggregate-decode-plan だけで ERROR marker / placeholder / missing の結果状態を読めるようにした。converter の同期 decode、個別行ログ量、placeholder / stale 判定の意味は変えていない。
- 2026-06-25 Worker Faraday(Image): image load / decode / decode plan ログへ `image_key` を追加し、role / revision / visible 文脈だけでなく対象キーも同じ行で追えるようにした。`ThumbnailPath` はログへ出さず、decode、placeholder、stale 判定、converter 挙動は変えていない。
- 保存 hot path は、UI操作中に同期 `Save()` や score / tag の直接DB更新へ戻らないことを source policy で固定した。
- view_count と movie_path は UI 表示値を先に反映し、DB 保存を背景へ送ることを source policy で固定した。
- skin profile write は UI hot path を enqueue のみに保ったまま、queue / persister / fallback 失敗時だけ cache と `skin-db` ログへ `dirty=true failed=true retryable=true` を出す入口を追加した。
- bookmark add / delete / view_count は既存背景経路を維持し、DB write 失敗時だけ軽量状態と `bookmark persist failed` ログへ `dirty=true failed=true retryable=true` を出す入口を追加した。
- `PersistenceFailureNotificationPolicy` を追加し、settings / score / tag / view_count / movie_path / bookmark / skin profile の保存失敗を `dirty` / `failed` / `retryable` / `notify_ui` の共通語彙へ寄せた。profile / bookmark / DB値系は retryable dirty として log-only、system 系の非 retryable 失敗だけ UI 通知候補として判定できる。同期 `Save()` や DB write は UI hot path へ戻していない。
- `PersistenceWriteRequest` / `PersistenceWriteResult` を追加し、application settings、player volume、playback stats、bookmark add / delete、score、tag、movie_path、skin profile の保存ログへ `write_kind` / `write_reason` / `queue_key` / `write_succeeded` / `failure_kind` を出せるようにした。保存実行順や hot path は変えない。
- 2026-06-19 Worker F: application settings / player volume の保存成功時も `PersistenceWriteResult.FromSuccess(...)` の共通 fields を1行だけ出し、失敗 / no-persist / background save 順序は変えていない。
- 2026-06-19 Worker I: playback stats 保存成功時も `PersistenceWriteResult.FromSuccess(...)` の共通 fields を1行だけ出し、DB更新順、`Task.Run`、例外処理、Player surface の hot path は変えていない。
- 2026-06-19 Worker L: skin state persister の system / profile 成功時も `BuildWriteSuccessResultLogFields(...)` の共通 fields を出し、batch、dedupe、DB書き込み順、cache persisted / fault の意味は変えていない。
- 2026-06-19 Worker R: score / tag / movie_path の背景保存成功ログも `PersistenceWriteRequest.BuildWriteSuccessResultLogFields(...)` 経由へ寄せた。DB write 順、`Task.Run`、失敗ログ、retryable dirty の意味は変えていない。
- 2026-06-25 Worker C: `PersistenceWriteResult.FromSuccess(...)` も `dirty=false failed=false retryable=false notify_ui=false` を出すようにし、保存成功 / 失敗を `write_succeeded` と同じ状態語彙で読めるようにした。保存順、UI hot path、失敗時 dirty の意味は変えていない。
- 2026-06-25 Worker Noether: `PersistenceWriteResult` の結果ログへ `persist_state=persisted|dirty-retryable|failed-notify|failed` を追加し、成功、retryable dirty、UI通知候補失敗を1語で読めるようにした。保存順、queue、background 化、UI 通知条件、DB write の意味は変えていない。
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
- 2026-06-25 Worker D: thumbnail queue の既存 failure / skip 系ログへ request / progress / result の代表 fields を1行で併記する `BuildWorkerQueueLogFields(...)` を追加した。rescue worker / watch metadata probe も input_count / capability_count / 診断文脈 / result metrics を既存 helper fields で読めるようにし、IPC方式や外部プロセス構造は変えていない。
- 2026-06-25 Worker Meitner: Thumbnail Queue の request / queue 代表ログへ `capability_count` と `diagnostic_context_count` を追加し、入力数だけでなく capability と診断文脈の規模を同じ行で読めるようにした。DTO schema、queue 実行順、IPC方式、外部プロセス方式は変えていない。
- 2026-06-25 Worker Harvey: watch metadata probe の request / probe 統合ログへ `diagnostic_context_count` を追加し、`capability_count` / `input_count` と並べて診断文脈の規模を読めるようにした。DTO schema、probe 実行順、DB 更新順、IPC 方式は変えていない。
- 2026-06-25 Worker Faraday: rescue worker request ログへ `diagnostic_context_count` を追加し、thumbnail queue / watch metadata probe と同じ Worker 契約規模語彙で読めるようにした。rescue worker JSON schema、process launch、failfast、IPC方式、外部プロセス方式は変えていない。
- 2026-06-25 Worker Anscombe: thumbnail queue / rescue worker / watch metadata probe の result 系ログへ `metric_count` を追加し、metrics の有無と規模を結果行だけで確認できるようにした。DTO schema、IPC方式、実行順、通常成功ログ量は変えていない。
- Watcher change set は `WatchUiApplyRequest` へ畳んでから full fallback / in-memory ReadModel 再計算へ流し、Watcher 側が表示 collection を直接 apply しない禁止線を source policy で固定した。
- Player surface 操作は、`Properties.Settings.Default.Save()`、DB write、設定保存 queue を直接呼ばない禁止線を source policy で固定した。surface と保存の分離を壊さず、既存の user-priority と保存方針を維持する。
- 2026-06-19 Worker P: Player 再生状態は `SetPlayerPlaybackActive(...)` に集約し、実際に active が変わった時だけ `operation_reason=player-playback` と reason をログへ出す。Everything poll の遅延理由と Player 操作ログを同じ語彙で突き合わせられる。
- 2026-06-25 Worker G: 外部 skin host refresh の queue / deferred / rejected / batch flush / begin ログへ `core_route=skin-refresh`、`refresh_reason`、`request_trace`、`definition_mode` を併記した。Header Reload / fallback retry / same-document skip / catalog freshness は変えていない。
- 2026-06-25 Worker H: `SetPlayerPlaybackActive(...)` の状態遷移ログへ `core_route=player-playback`、`player_surface`、`active`、`operation_reason`、`reason` を helper 経由で併記した。同状態 return、user-priority release、WebView navigation、保存分離は変えていない。
- 2026-06-25 Worker Kant: Watcher の `watch ui apply request:` ログへ `core_route=watch-ui-apply`、`watch_apply_kind`、`watch_reason`、`operation_reason` を helper 経由で併記した。`WatchUiApplyRequest` の構造、query-only / full fallback 判定、scheduler admission、実行順は変えていない。
- 2026-06-25 Worker Leibniz: 外部 skin refresh の core route helper へ `operation_reason=skin.host-refresh` を追加し、`refresh_reason` / `request_trace` / `definition_mode` と同じ行で skin refresh の実行理由を読めるようにした。
- 2026-06-25 Worker Banach: Player core route ログへ `player_transition=start|stop` を追加し、`active=true/false` と開始 / 停止の意味を同じ行で読めるようにした。
- 2026-06-25 Worker Galileo: Persistence write の共通 fields へ `persist_contract=persistence-write-v1` を追加し、settings / bookmark / skin profile などの保存ログを契約単位で grep できるようにした。
- 2026-06-25 Worker Turing: Thumbnail Queue / Rescue Worker / Watch metadata probe の Worker契約ログへ `worker_contract=worker-job-v1` を追加し、request / progress / result / combined logs を同じ契約識別子で追えるようにした。
- 2026-06-25 Worker Chandrasekhar: ReadModel / Watch diff apply plan fields へ `diff_contract=readmodel-diff-v1` を追加し、diff apply と full fallback のログを契約単位で追えるようにした。
- 2026-06-25 Worker Curie(Scheduler): Scheduler admission / take / timeout fields へ `scheduler_contract=scheduler-v1` を追加し、入場、実行引き渡し、timeout release を同じ契約識別子で追えるようにした。
- 2026-06-25 Worker Hilbert: UI Shell snapshot fields へ `ui_shell_contract=ui-shell-v1` を追加し、user-priority begin / end と search / sort 入力入口を契約単位で追えるようにした。
- 2026-06-25 Worker McClintock: Image load / decode / decode plan ログへ `image_contract=image-pipeline-v1` を追加し、画像 pipeline の各結果ログを契約単位で追えるようにした。
- 2026-06-25 Worker Parfit: UI Shell / ReadModel Diff / Scheduler / Image の contract 識別子を source policy で固定し、helper 経由と重複なしの線を戻さないようにした。production code は変えていない。
- 2026-06-25 Worker Hypatia: Persistence / Worker / Skin / Player / Watcher の contract 識別子と core route fields を source policy で固定した。保存順、worker実行順、skin / Player / Watcher の実行入口は変えていない。
- 2026-06-25 Worker Newton: `DebugRuntimeLogEvidencePolicy` を追加し、採取済み `debug-runtime.log` の UI Shell / Diff / Scheduler / Image / Persistence / Worker / Skin / Player / Watcher の evidence token 欠落を安定順で確認できるようにした。ファイルI/Oや実機完了判定は入れていない。
- 2026-06-25 Worker Volta: `DebugRuntimeLogRunSlicePolicy` を追加し、sequence 巻き戻りから同一ログファイル内の最新起動runだけを切り出せるようにした。ログ出力形式や app runtime の挙動は変えていない。
- 2026-06-25 Worker Linnaeus: `DebugRuntimeLogRunSliceResult.BuildSummaryText()` を追加し、最新runの行数、sequence 範囲、reset 数を `log_run_lines=...` の1行で確認できるようにした。切り出し判定と実機完了判定は変えていない。
- 2026-06-25 Worker Carson: `DebugRuntimeLogPhase0EvidencePolicy` を追加し、startup first-page / input ready、search / sort 入力、Player / Watcher / Image / Persistence / Worker / Skin の Phase0 evidence 欠落を確認できるようにした。ファイルI/Oや UI 接続は入れていない。
- 2026-06-25 Worker Bacon: Phase0 evidence に `scroll-input` と `thumbnail-worker` を追加し、正本の search / sort / scroll / Player / watch / thumbnail / skin 確認対象と token 集計を揃えた。実機ログ採取や Phase 完了判定は入れていない。
- 2026-06-25 Worker Bernoulli: Logタブ preview の冒頭へ最新run summary、contract evidence summary、Phase0 evidence summary を表示するようにした。読み込みは既存どおり背景 helper 側で行い、XAML は変えていない。
- 2026-06-25 Worker Noether(Phase0): `DebugRuntimeLogRunWindowPolicy` を追加し、最新runの開始 / 終了 timestamp と elapsed_ms を `log_run_window=...` で確認できるようにした。採取runの同一性を見やすくするだけで、ログ出力や完了判定は変えていない。
- 2026-06-25 Worker Wegener: `DebugRuntimeLogPhase0NextActionPolicy` を追加し、Phase0 evidence の不足keyを `phase0_next_actions=...` へ畳んだ。採取時に次に操作する対象を示すだけで、実機採取の代替にはしない。
- 2026-06-25 Worker Pasteur: search / sort 入力入口が `BuildUiShellInputLogMessage(...)` 経由で `ui shell input` と snapshot fields を出すことを source policy で固定した。検索実行順、sort 実行順、UIイベント面は変えていない。
- 2026-06-25 Worker Jason: `IMM_PHASE0_LOG_AUDIT_LIVE=1` の時だけ採取済み `debug-runtime.log` を `DebugRuntimeLogAuditSummaryPolicy` で検証する live audit test を追加した。実機採取の代替ではなく、採取後に不足keyを失敗メッセージで読む導線とする。
- 2026-06-25 Worker Lagrange: Scheduler admission ログへ `accepted` / `target_index`、take ログへ `has_request` を追加し、enqueue / replace / reject / released の判定結果を同じ行で読めるようにした。admission / queue / take の挙動は変えていない。
- ReadModel 計算、一覧 apply、要求制御、表示レコード生成、MainDB runtime、起動 / dock layout / lifecycle、入力 routing は partial / helper 分離済み。
- `FilterAndSort(..., true)` は起動 fallback と段階ロード中 sort の2箇所、直書き `Refresh();` は startup first page と選択変化互換 helper の2箇所だけに固定されている。
- 次の段階は、新しい巨大 core を作ることではなく、既存境界の上に小さな契約を積み、実機ログで支配要因を確認しながら差し替えること。

### 2.1 2026-06-25 PM親レビュー

- Worker A / B の2本を並走し、Phase 3 Scheduler と Phase 4 Image Pipeline をそれぞれ小口で進めた。
- 親レビューでは、Worker A は実行順を変えず kana backfill の scheduler admission / released / pending_count 証跡を足す変更として採用した。汎用ジョブ実行器化や timeout / drain 拡大は行っていない。
- 親レビューでは、Worker B は ThumbnailError 背景集計に `ImageDecodePlanResult` を追加し、ERROR marker / placeholder / missing を `decode_attempted=false` の decode 計画語彙でも読める変更として採用した。converter の同期 decode 挙動と個別行ログ量は変えていない。
- baseline で赤かった `ThumbnailErrorSourceTests` / `TagKanaLocalRefreshSourceTests` は、実装赤ではなく `%TEMP%` 出力時に repo root を解決できない source policy helper drift だったため、`CallerFilePath` / current directory を探索候補へ足す最小補正として扱った。
- 親検証は focused test 127件成功、Release x64 build 成功、`git diff --check` 成功。Release build の `NETSDK1206` 2件は既存の SQLitePCLRaw RID 警告として扱う。
- 実機 `debug-runtime.log` で `kana backfill scheduler released`、`pending_count`、`error tab image aggregate`、`aggregate-decode-plan` をまだ確認していないため、Phase 3 / 4 は完了扱いにしない。

### 2.2 2026-06-25 PM親レビュー Phase5/6

- Worker C / D の2本を並走し、Phase 5 Persistence Pipeline と Phase 6 Worker 契約を小口で進めた。
- 親レビューでは、Worker C は保存成功ログにも `dirty=false failed=false retryable=false notify_ui=false` を載せ、失敗ログと同じ状態語彙で読める変更として採用した。保存順、UI hot path、retryable dirty の意味は変えていない。
- 親レビューでは、Worker D は thumbnail queue の failure / skip 系ログへ request / progress / result の代表 fields を1行併記し、rescue worker / watch metadata probe の helper fields も input_count / capability_count / 診断文脈 / result metrics まで読める変更として採用した。IPC sidecar や外部プロセス構造変更は入れていない。
- 親検証は focused test 148件成功、Release x64 build 成功、`git diff --check` 成功。Release build の `NETSDK1206` 2件は既存の SQLitePCLRaw RID 警告として扱う。
- 実機 `debug-runtime.log` で `write_succeeded=true/false` と dirty / failed / retryable / notify_ui、Worker DTO fields が同一操作中の支配要因説明に足りるかをまだ確認していないため、Phase 5 / 6 は完了扱いにしない。

### 2.3 2026-06-25 PM親レビュー Phase1/2

- Worker E / F の2本を並走し、Phase 1 UI Shell 入力契約と Phase 2 ReadModel Store / Diff-first をそれぞれ小口で進めた。
- 親レビューでは、Worker E は `UiOperationSnapshot` の共通ログ fields を追加し、user-priority begin / end で search / sort / player 中の入力状態を同じ語彙で読める変更として採用した。実行順、catch-up、timeout 判定は変えていない。
- 親レビューでは、Worker F は watch apply request ログへ source / applied の changed path 数と `diff_change_set` を併記し、単件 change set と full fallback 理由を同じ行で読める変更として採用した。query-only / full fallback 判定や通常成功ログ量は変えていない。
- 親検証は focused test 262件成功、Release x64 build 成功、`git diff --check` 成功。Release build の `NETSDK1206` 2件は既存の SQLitePCLRaw RID 警告として扱う。
- 実機 `debug-runtime.log` で user-priority snapshot fields と、watch 1件追加 / rename の `diff_change_set=single`、`diff_apply_kind`、`diff_full_fallback_reason` をまだ確認していないため、Phase 1 / 2 は完了扱いにしない。

### 2.4 2026-06-25 PM親レビュー Phase7 Skin/Player

- Worker G / H の2本を並走し、Phase 7 Skin / Player / Watcher の Core 接続のうち skin refresh と Player playback を小口で進めた。
- 親レビューでは、Worker G は外部 skin refresh の queue / deferred / rejected / batch flush / begin に core route、refresh reason、request trace、definition mode を併記する変更として採用した。Header Reload、fallback retry、same-document skip、catalog freshness の意味は変えていない。
- 親レビューでは、Worker H は Player 再生状態遷移ログに core route、surface、active、operation reason、reason を併記する変更として採用した。同状態 return、user-priority release、WebView navigation、保存分離は変えていない。
- 親検証は focused test 110件成功、Release x64 build 成功、`git diff --check` 成功。Release build の `NETSDK1206` 2件は既存の SQLitePCLRaw RID 警告として扱う。
- 実機 `debug-runtime.log` で skin refresh の `core_route=skin-refresh` / scheduler admission / `definition_mode` と、Player start / pause / end の `core_route=player-playback` / `player_surface` をまだ確認していないため、Phase 7 は完了扱いにしない。

### 2.5 2026-06-25 PM親レビュー Phase6/7 競合回避小口

- UIシンプル化が別スレで作業中のため、Worker Kant / Meitner の2本は `Watcher` と `src/IndigoMovieManager.Thumbnail.Queue` に限定し、XAML、Settings 画面、入力イベント表面へ触れない方針で進めた。
- 親レビューでは、Worker Kant は Watcher apply request の既存ログへ core route、apply kind、watch reason、operation reason を併記する変更として採用した。query-only / full fallback 判定、scheduler admission、`InvokeFilterAndSortForWatch(...)` / `RefreshMovieViewFromCurrentSourceAsync(...)` の実行順は変えていない。
- 親レビューでは、Worker Meitner は Thumbnail Queue Worker契約の代表ログへ `capability_count` と `diagnostic_context_count` を足す変更として採用した。DTO schema、queue実行順、IPC方式、外部プロセス方式は変えていない。
- 親検証は focused test 149件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功。Release build の `NETSDK1206` 2件は既存の SQLitePCLRaw RID 警告として扱う。
- 実機 `debug-runtime.log` で `core_route=watch-ui-apply` / `watch_apply_kind` / `operation_reason` と、Thumbnail Queue の `capability_count` / `diagnostic_context_count` が failure / skip 系ログへ出ることをまだ確認していないため、Phase 6 / 7 は完了扱いにしない。

### 2.6 2026-06-25 PM親レビュー Phase3/6 競合回避小口

- UIシンプル化は別スレの `0febff9` としてコミット済みのため、Worker Harvey / Lagrange は Watcher metadata probe と Scheduler policy のログ契約に限定した。
- 親レビューでは、Worker Harvey は watch metadata probe の request / probe 統合ログへ `diagnostic_context_count` を追加する変更として採用した。DTO schema、probe 実行順、DB 更新順、IPC 方式は変えていない。
- 親レビューでは、Worker Lagrange は Scheduler admission / take ログへ `accepted` / `target_index` / `has_request` を追加する変更として採用した。admission / queue / take の挙動は変えていない。
- 親検証は focused test 49件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功。Release build の `NETSDK1206` 2件は既存の SQLitePCLRaw RID 警告として扱う。
- 実機 `debug-runtime.log` で Scheduler の `accepted` / `target_index` / `has_request` と watch metadata probe の `diagnostic_context_count` をまだ確認していないため、Phase 3 / 6 は完了扱いにしない。

### 2.7 2026-06-25 PM親レビュー Phase4/5 競合回避小口

- 親レビュー時点では UIシンプル化の未コミット差分が `Views/Main` に残っていたため、Worker Orion / Noether は `UpperTabs\Common`、`Persistence`、対応 tests だけに限定し、XAML、設定画面、入力イベント表面へ触れない方針で進めた。
- 親レビューでは、Worker Orion は `ImageDecodePlanLogFields.Build(...)` へ load 状態 fields を追加する変更として採用した。converter の同期 decode、placeholder、missing、ERROR marker、stale 判定、個別行ログ量は変えていない。
- 親レビューでは、Worker Noether は `PersistenceWriteResult` の結果ログへ `persist_state` を追加する変更として採用した。保存順、queue、background 化、UI通知条件、DB write の意味は変えていない。
- 親検証は、未コミットの `Views/Main` 差分を混ぜないため sibling の clean worktree で実施した。focused test 56件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功、対象4ファイルの UTF-8 BOMなし + LF を確認済み。
- 実機 `debug-runtime.log` で aggregate-decode-plan の `image_result_revision` / `resolved` / `placeholder` / `stale` / `failure_reason` と、保存ログの `persist_state` をまだ確認していないため、Phase 4 / 5 は完了扱いにしない。

### 2.8 2026-06-25 PM親レビュー Phase2/6 競合回避小口

- 親レビュー時点では UIシンプル化の未コミット差分が `Views/Main` と `Views/Settings` に残っていたため、Worker Curie / Faraday は `MovieViewDiff` と `ThumbnailRescueWorkerJobJsonClient`、対応 tests だけに限定し、XAML、設定画面、入力イベント表面へ触れない方針で進めた。
- 親レビューでは、Worker Curie は diff ログへ `diff_changed_total` を追加する変更として採用した。diff apply 判定、collection apply、fallback 条件、UI 挙動は変えていない。
- 親レビューでは、Worker Faraday は rescue worker request ログへ `diagnostic_context_count` を追加する変更として採用した。JSON schema、process launch、failfast、IPC方式、外部プロセス方式は変えていない。
- 親検証は、未コミットの UI / Settings 差分を混ぜないため sibling の clean worktree で実施した。focused test 84件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功、対象5ファイルの UTF-8 BOMなし + LF を確認済み。Release build の `NETSDK1206` 2件は既存の SQLitePCLRaw RID 警告として扱う。
- 実機 `debug-runtime.log` で ReadModel / watch の `diff_changed_total` と rescue worker request の `diagnostic_context_count` をまだ確認していないため、Phase 2 / 6 は完了扱いにしない。

### 2.9 2026-06-25 PM親レビュー Phase3/4 ログ文脈補強小口

- UIシンプル化は `862ff6a` でコミット済みだが、Worker Zeno / Cicero は引き続き XAML、設定画面、テーマ辞書へ触れず、Scheduler policy と Image request 契約だけに限定した。
- 親レビューでは、Worker Zeno は Scheduler timeout ログへ `timeout_released` を追加する変更として採用した。timeout 判定、queue drain、pending request の解放条件は変えていない。
- 親レビューでは、Worker Cicero は image load / decode ログへ `visible_priority` / `image_cache_policy` / `should_decode` を追加する変更として採用した。converter、同期 decode、placeholder、missing、ERROR marker、stale 判定は変えていない。
- 親検証は focused test 28件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功、対象4ファイルの UTF-8 BOMなし + LF を確認済み。Release build の `NETSDK1206` 2件は既存の SQLitePCLRaw RID 警告として扱う。
- 実機 `debug-runtime.log` で Scheduler の `timeout_released` と、stale discard / aggregate-decode-plan の `visible_priority` / `image_cache_policy` / `should_decode` をまだ確認していないため、Phase 3 / 4 は完了扱いにしない。

### 2.10 2026-06-25 PM親レビュー Phase1/7 入力入口とPlayer core補強小口

- Worker Averroes / Gibbs は XAML、設定画面、テーマ辞書へ触れず、UI Shell 入力ログと Player core route ログだけに限定した。
- 親レビューでは、Worker Averroes は search / sort の入口ログへ `ui shell input`、`operation_reason`、`trigger_reason`、`UiOperationSnapshot` fields を追加する変更として採用した。SearchExecutor の実行順、履歴保存、FilterAndSort の条件、sort full reload 許容線は変えていない。
- 親レビューでは、Worker Gibbs は Player core route ログへ `player_surface_ready` を追加する変更として採用した。Start / Pause / Stop、WebView navigation、保存分離、user-priority release、surface 切替の意味は変えていない。
- 親検証は focused test 109件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功、対象7ファイルの UTF-8 BOMなし + LF を確認済み。`BaseOutputPath` を `%TEMP%` へ逃がした初回テストは source policy test が repo root を辿れず失敗したため、標準出力先で再実行して成功した。
- 実機 `debug-runtime.log` で search / sort の `ui shell input` snapshot fields と、Player の `player_surface_ready` をまだ確認していないため、Phase 1 / 7 は完了扱いにしない。

### 2.11 2026-06-25 PM親レビュー Phase2/6 追加ログ観測小口

- Worker Fermat / Anscombe は XAML、設定画面、テーマ辞書へ触れず、watch change set ログと Worker result ログだけに限定した。
- 親レビューでは、Worker Fermat は watch apply request の change set ログへ `diff_changed_total` を追加する変更として採用した。source / applied 件数と `diff_change_set` の意味、query-only / full fallback 判定、scheduler admission、UI apply の実行順は変えていない。
- 親レビューでは、Worker Anscombe は thumbnail queue / rescue worker / watch metadata probe の result 系ログへ `metric_count` を追加する変更として採用した。DTO schema、IPC方式、外部プロセス方式、通常成功ログ量は変えていない。
- 親検証は focused test 202件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功、対象10ファイルの UTF-8 BOMなし + LF、ローカル固有情報スキャン一致なしを確認済み。
- 実機 `debug-runtime.log` で watch 1件追加 / rename の `diff_changed_total=1` と、thumbnail queue / rescue worker / watch metadata probe の `metric_count` が支配要因確認に足りるかをまだ確認していないため、Phase 2 / 6 は完了扱いにしない。

### 2.12 2026-06-25 PM親レビュー Phase3/4 timeoutとImage対象キー小口

- Worker Locke / Faraday(Image) は XAML、設定画面、テーマ辞書へ触れず、Scheduler timeout release ログと Image request ログだけに限定した。
- 親レビューでは、Worker Locke は timeout release ログへ `sequence` と `pending_count_after` を追加する変更として採用した。timeout 判定、pending 削除順、queue 操作、戻り値の意味は変えていない。
- 親レビューでは、Worker Faraday(Image) は image load / decode / decode plan ログへ `image_key` を追加する変更として採用した。`ThumbnailPath` はログへ出さず、decode、placeholder、stale 判定、converter 挙動は変えていない。
- 親検証は focused test 219件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功、対象6ファイルの UTF-8 BOMなし + LF、ローカル固有情報スキャン一致なしを確認済み。
- 実機 `debug-runtime.log` で Scheduler timeout release の `sequence` / `pending_count_after` と、stale discard / aggregate-decode-plan の `image_key` が支配要因確認に足りるかをまだ確認していないため、Phase 3 / 4 は完了扱いにしない。

### 2.13 2026-06-25 PM親レビュー Phase7 Skin/Player core route小口

- Worker Leibniz / Banach は XAML、設定画面、テーマ辞書へ触れず、skin refresh と Player playback の core route ログ補強だけに限定した。
- 親レビューでは、Worker Leibniz は外部 skin refresh の core route helper へ `operation_reason=skin.host-refresh` を追加する変更として採用した。queue / deferred / rejected / batch / begin の入口、Header Reload、fallback retry、same-document skip、catalog freshness の意味は変えていない。
- 親レビューでは、Worker Banach は Player 再生状態遷移ログへ `player_transition=start|stop` を追加する変更として採用した。Start / Pause / Stop、WebView navigation、保存分離、user-priority release、surface 切替の意味は変えていない。
- 親検証は、WPF統合を含む focused test が 201件合格表示後に testhost 終了時クラッシュでコマンド失敗扱い、WPF統合を外したポリシー系 focused test 108件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功、対象5ファイルの UTF-8 BOMなし + LF、ローカル固有情報スキャン一致なしを確認済み。
- 実機 `debug-runtime.log` で skin refresh の `operation_reason=skin.host-refresh` と Player の `player_transition=start|stop` をまだ確認していないため、Phase 7 は完了扱いにしない。

### 2.14 2026-06-25 PM親レビュー Phase5/6 contract識別子小口

- Worker Galileo / Turing は UIシンプル化別スレと競合しないよう、Persistence helper と Worker契約 adapter / tests だけに限定した。
- 親レビューでは、Worker Galileo は `PersistenceWriteRequest.BuildLogFields()` へ `persist_contract=persistence-write-v1` を追加する変更として採用した。保存順、queue key、retryable 判定、dirty / failed / notify UI の意味は変えていない。
- 親レビューでは、Worker Turing は Thumbnail Queue / Rescue Worker / Watch metadata probe の Worker契約ログへ `worker_contract=worker-job-v1` を追加する変更として採用した。DTO schema、queue実行順、IPC方式、外部プロセス起動、probe 実行順は変えていない。
- 親検証は focused test 95件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功、対象11ファイルの UTF-8 BOMなし + LF、ローカル固有情報スキャン一致なしを確認済み。
- 実機 `debug-runtime.log` で `persist_contract=persistence-write-v1` と `worker_contract=worker-job-v1` が success / failure / skip 系ログを横断して追えるかをまだ確認していないため、Phase 5 / 6 は完了扱いにしない。

### 2.15 2026-06-25 PM親レビュー Phase2/3 contract識別子小口

- Worker Chandrasekhar / Curie(Scheduler) は UIシンプル化別スレと競合しないよう、ReadModel diff helper と Scheduler policy helper / tests だけに限定した。
- 親レビューでは、Worker Chandrasekhar は `MovieViewDiffApplyPolicy.BuildDiffApplyPlanLogFields(...)` へ `diff_contract=readmodel-diff-v1` を追加する変更として採用した。diff apply 判定、full fallback 理由、collection apply、watch query-only 判定は変えていない。
- 親レビューでは、Worker Curie(Scheduler) は Scheduler admission / take / timeout fields へ `scheduler_contract=scheduler-v1` を追加する変更として採用した。入場、置換、reject、take、timeout release の判定と queue 操作は変えていない。
- 親検証は focused test 248件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功、対象4ファイルの UTF-8 BOMなし + LF、ローカル固有情報スキャン一致なしを確認済み。
- 実機 `debug-runtime.log` で `diff_contract=readmodel-diff-v1` と `scheduler_contract=scheduler-v1` が watch 1件追加 / rename、scheduler admission / take / timeout release を横断して追えるかをまだ確認していないため、Phase 2 / 3 は完了扱いにしない。

### 2.16 2026-06-25 PM親レビュー Phase1/4 contract識別子小口

- Worker Hilbert / McClintock は UIシンプル化別スレと競合しないよう、UI Shell snapshot helper と Image pipeline log helper / tests だけに限定した。
- 親レビューでは、Worker Hilbert は `UiOperationPriorityPolicy.BuildSnapshotLogFields(...)` へ `ui_shell_contract=ui-shell-v1` を追加する変更として採用した。user-priority、defer、catch-up、poll delay の判定は変えていない。
- 親レビューでは、Worker McClintock は Image load / decode / decode plan ログへ `image_contract=image-pipeline-v1` を追加する変更として採用した。decode、placeholder、missing、ERROR marker、stale 判定、converter 挙動は変えていない。
- 親検証は focused test 138件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功、対象5ファイルの UTF-8 BOMなし + LF、ローカル固有情報スキャン一致なしを確認済み。
- 実機 `debug-runtime.log` で `ui_shell_contract=ui-shell-v1` と `image_contract=image-pipeline-v1` が search / sort / player 入口、stale discard、aggregate-decode-plan を横断して追えるかをまだ確認していないため、Phase 1 / 4 は完了扱いにしない。

### 2.17 2026-06-25 PM親レビュー Phase0 contract source policy固定

- Worker Parfit / Hypatia は UIシンプル化別スレと競合しないよう、production code へ触れずテストだけに限定した。
- 親レビューでは、Worker Parfit は UI Shell / ReadModel Diff / Scheduler / Image の contract 識別子を source policy で固定する変更として採用した。`UiShellContract`、`DiffContractReadModelDiffV1`、`SchedulerContractLogField`、Image decode / load builder の contract 出力を戻さない線にした。
- 親レビューでは、Worker Hypatia は Persistence / Worker / Skin / Player / Watcher の contract 識別子と core route fields を source policy で固定する変更として採用した。保存順、worker実行順、skin refresh、Player playback、Watcher apply の実行入口は変えていない。
- 親検証は focused test 159件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功。Release build は警告0件で完了した。
- 今回は実機 `debug-runtime.log` の新規採取ではなく戻り防止の強化なので、search / sort / scroll / Player / watch / thumbnail / skin を同一 Release run で説明できるまで Phase 0 は完了扱いにしない。

### 2.18 2026-06-25 PM親レビュー Phase0 debug-runtime log証跡policy小口

- Worker Newton / Volta は UIシンプル化別スレと競合しないよう、`Infrastructure` の純粋 policy と対応 tests だけに限定した。
- 親レビューでは、Worker Newton は `DebugRuntimeLogEvidencePolicy` により、採取済みログ内の `ui_shell_contract`、`diff_contract`、`scheduler_contract`、`image_contract`、`persist_contract`、`worker_contract`、skin / player / watch core route の evidence token を安定順で確認する変更として採用した。ファイルI/O、LogタブUI、実機完了判定は入れていない。
- 親レビューでは、Worker Volta は `DebugRuntimeLogRunSlicePolicy` により、`#000001` 形式の sequence 巻き戻りから最新起動runだけを切り出す変更として採用した。`DebugRuntimeLog` の出力形式、throttle、書き込み先、runtime 挙動は変えていない。
- 親検証は focused test 8件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功。Release build は警告0件で完了した。
- これで実機ログ採取後の抜け漏れ確認は少し機械化できたが、まだ実機 `debug-runtime.log` の新規採取そのものは行っていないため Phase 0 は完了扱いにしない。

### 2.19 2026-06-25 PM親レビュー Phase0 最新run要約と操作evidence小口

- Worker Linnaeus / Carson は UIシンプル化別スレと競合しないよう、`Infrastructure` の純粋 policy と対応 tests だけに限定した。Logタブ、XAML、MainWindow 入力面には触れていない。
- 親レビューでは、Worker Linnaeus は `DebugRuntimeLogRunSliceResult.BuildSummaryText()` により、最新run切り出し結果の行数、sequence 有無、sequence 範囲、reset 数を `log_run_lines=...` の1行で確認する変更として採用した。run slice 判定やログ読み込み処理は変えていない。
- 親レビューでは、Worker Carson は `DebugRuntimeLogPhase0EvidencePolicy` により、startup first-page / input ready、search / sort 入力、Player / Watcher / Image / Persistence / Worker / Skin の evidence token 欠落を安定順で確認する変更として採用した。これは採取後の抜け漏れ確認 helper であり、実機ログ採取や Phase 完了判定ではない。
- 親検証は focused test 14件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功。Release build は警告0件で完了した。
- 実機 `debug-runtime.log` の新規採取と `DebugRuntimeLogRunSlicePolicy` / `DebugRuntimeLogPhase0EvidencePolicy` による実ログ確認はまだ行っていないため、Phase 0 は完了扱いにしない。

### 2.20 2026-06-25 PM親レビュー Phase0 Logタブ証跡summary接続小口

- Worker Bacon / Bernoulli は UIシンプル化別スレと競合しないよう、Phase0 evidence policy と Logタブ preview helper / source policy test だけに限定した。
- 親レビューでは、Worker Bacon は `DebugRuntimeLogPhase0EvidencePolicy` へ `scroll-input=page scroll end:` と `thumbnail-worker=worker_kind=thumbnail-create` を追加する変更として採用した。正本の search / sort / scroll / Player / watch / thumbnail / skin の確認対象と `phase0_log_evidence` の token を揃えたが、実機ログ採取や Phase 完了判定は入れていない。
- 親レビューでは、Worker Bernoulli は Logタブ preview 冒頭に `log_run_lines=...`、`log_evidence=...`、`phase0_log_evidence=...` を表示する変更として採用した。既存の `Task.Run` 背景読み込み、requestId 後着ガード、巨大ログ末尾読みの方針は維持し、XAML は変えていない。
- 親検証は focused test 15件成功、Release x64 build 成功、対象コミット範囲の `git diff --check` 成功。Release build は警告0件で完了した。
- Logタブで採取済みログの抜け漏れは見やすくなったが、同一 Release run の search / sort / scroll / Player / watch / thumbnail / skin 操作ログをまだ採取していないため、Phase 0 は完了扱いにしない。

### 2.21 2026-06-25 PM親レビュー Phase0 監査summary実出力固定小口

- Worker Heisenberg / Hooke は UIシンプル化別スレと競合しないよう、`Infrastructure` の純粋 policy、Logタブ preview helper、対応 tests だけに限定した。XAML、Settings 画面、入力イベント面には触れていない。
- 親レビューでは、Worker Heisenberg は `DebugRuntimeLogAuditSummaryPolicy` により、最新run切り出し、contract evidence、Phase0 evidence を同一runに対する3行summaryとして合成する変更として採用した。File I/O、WPF、Dispatcher は持たせず、実機ログ採取後の確認入口だけを増やした。
- 親レビューでは、Worker Hooke は `BuildLogPreviewTextWithSummary(...)` の実出力をテスト化し、先頭3行の固定順と古いrun tokenを最新run evidenceへ数えない挙動を固定する変更として採用した。親側で Logタブの手組みsummaryを `DebugRuntimeLogAuditSummaryPolicy` へ寄せ、summary構成の正本を1箇所にした。
- 親検証は focused test 20件成功、Release x64 build 成功、対象差分の `git diff --check` 成功。Release build は既存系の `NETSDK1206` 警告2件で完了した。
- 監査summaryの出力保証は強くなったが、同一 Release run の search / sort / scroll / Player / watch / thumbnail / skin 操作ログをまだ採取していないため、Phase 0 は完了扱いにしない。

### 2.22 2026-06-25 PM親レビュー Phase0 run時間窓と次アクション小口

- Worker Noether(Phase0) / Wegener は UIシンプル化別スレと競合しないよう、`Infrastructure` の純粋 policy と対応 tests だけに限定した。XAML、Settings 画面、入力イベント面、ログ出力runtimeには触れていない。
- 親レビューでは、Worker Noether(Phase0) は `DebugRuntimeLogRunWindowPolicy` により、最新run内の先頭 / 末尾 timestamp、elapsed_ms、timestamp行数を `log_run_window=...` で読める変更として採用した。同一 Release run 採取の時間幅を確認する補助であり、実機完了判定は入れていない。
- 親レビューでは、Worker Wegener は `DebugRuntimeLogPhase0NextActionPolicy` により、Phase0 evidence の不足keyを `startup/search/sort/scroll/player/watch/image/persistence/thumbnail/skin` の次アクションへ畳む変更として採用した。worker と thumbnail-worker は `thumbnail` に、startup first-page / input-ready は `startup` にまとめ、採取手順を迷わせないようにした。
- 親側で `DebugRuntimeLogAuditSummaryPolicy` へ run時間窓と次アクションを接続し、Logタブ preview summary は `log_run_lines` / `log_run_window` / `log_evidence` / `phase0_log_evidence` / `phase0_next_actions` の5行になった。Logタブは既存どおり audit summary を表示するだけで、UIスレッドのファイルI/Oは増やしていない。
- 親検証は focused test 30件成功、Release x64 build 成功、対象差分の `git diff --check` 成功。Release build は警告0件で完了した。
- ローカルの既存 `debug-runtime.log` 最新runは search / watch 断片が中心で、同一 Release run の search / sort / scroll / Player / watch / thumbnail / skin 操作ログ一式はまだ揃っていないため、Phase 0 は完了扱いにしない。

### 2.23 2026-06-25 PM親レビュー Phase0 入力ログsource policyとlive audit導線

- Worker Pasteur / Jason は UIシンプル化別スレと競合しないよう、production code、XAML、Settings 画面へ触れず tests だけに限定した。
- 親レビューでは、Worker Pasteur は search / sort 入力入口が `BuildUiShellInputLogMessage(...)` 経由で `ui shell input` と snapshot fields を出すことを source policy で固定する変更として採用した。検索実行順、sort 実行順、user-priority の開始 / 終了条件は変えていない。
- 親レビューでは、Worker Jason は `IMM_PHASE0_LOG_AUDIT_LIVE=1` の時だけ採取済み `debug-runtime.log` を `DebugRuntimeLogAuditSummaryPolicy` で検証する live audit test を追加する変更として採用した。既定では skip し、採取済みログがない / 空 / timestampなし / Phase0 evidence不足の場合は `BuildSummaryText()` を含む失敗メッセージで次アクションを示す。
- 親検証は focused test 103件成功 / 1件skip、Release x64 build 成功、対象差分の `git diff --check` 成功。Release build は警告0件で完了した。
- 今回も実機同一runの新規採取ではない。live audit は採取後の検査導線であり、同一 Release run の search / sort / scroll / Player / watch / thumbnail / skin 操作ログ一式が揃うまで Phase 0 は完了扱いにしない。

### 2.24 2026-06-27 PM親レビュー Phase0 live audit完了条件とUI高密度ヘッダー固定

- Worker Boyle / Lovelace / Confucius は UI簡素化別スレの実装へ触れず、live audit と MainWindow 標準ヘッダーの source policy に限定した。
- 親レビューでは、Worker Boyle の live audit 完了条件を採用し、`ContractEvidence.IsComplete` と `Phase0Evidence.IsComplete` の両方を満たすまで Phase0 完了と見なさない線にした。
- 親レビューでは、Worker Lovelace の source policy を採用し、`IMM_PHASE0_LOG_AUDIT_LIVE`、`IMM_PHASE0_LOG_AUDIT_PATH`、`LOCALAPPDATA` 既定、`FileShare.ReadWrite | FileShare.Delete`、完了条件、失敗summaryを戻さないよう固定した。
- 親レビューでは、Worker Confucius の MainWindow 標準ヘッダー source policy を採用し、`MainHeaderStandardChromePanel` の 10列構成、26px帯の検索欄、DBパス表示、fallback notice、ヘッダーボタンと ComboBox の compact style を戻さないよう固定した。
- 親検証は focused test 99件成功 / 1件skip、Release x64 build 成功、警告0件で完了した。
- `IMM_PHASE0_LOG_AUDIT_LIVE=1` の実ログ audit は意図どおり失敗し、最新runは `log_run_lines=116/1789`、`log_run_window=2026-06-27T00:07:22.196..2026-06-27T00:08:08.740`、`log_evidence=2/9 missing=readmodel-diff,scheduler,image,worker,skin-core,player-core,watch-core`、`phase0_log_evidence=1/12 missing=startup-first-page,startup-input-ready,search-input,sort-input,scroll-input,player-core,watch-core,image-pipeline,worker,thumbnail-worker,skin-core` だった。次アクションは `startup,search,sort,scroll,player,watch,image,thumbnail,skin` で、Phase 0 はまだ完了扱いにしない。

### 2.25 2026-06-27 PM親レビュー Phase0 audit完了表示とPlayer入力snapshot

- Worker Arendt / Dirac は UI簡素化別スレと競合しないよう、audit infrastructure / tests と Player partial / source policy tests だけに限定した。XAML、Themes、Settings 画面には触れていない。
- 親レビューでは、Worker Arendt の `DebugRuntimeLogAuditSummary.IsComplete` と `phase0_audit_complete=true|false` summary 行を採用した。完了条件は `ContractEvidence.IsComplete && Phase0Evidence.IsComplete` で、`Phase0NextActions.IsComplete` だけでは完了扱いにしない。
- 親レビューでは、Worker Dirac の Player 入力 snapshot ログを採用した。`SetPlayerPlaybackActive(...)` の実遷移後だけ `BuildUiShellInputLogMessage(UiOperationPriorityPolicy.OperationReasonPlayerPlayback, reason, snapshot)` を `ui-priority` へ出し、既存の `player playback state changed: core_route=player-playback ...` は維持する。
- 親検証は focused test 77件成功 / 1件skip、Release x64 build 成功、警告0件で完了した。
- 今回も実機同一runの新規採取ではない。次の実機採取では Logタブ preview 先頭の `phase0_audit_complete=false` が、startup / search / sort / scroll / Player / watch / image / thumbnail / skin 操作後に `true` へ変わるかを確認する。

### 2.26 2026-06-27 PM親レビュー scroll入力snapshotとPhase0 scroll evidence移行

- Worker Mill / Godel は UI簡素化別スレと競合しないよう、Phase0 evidence policy と UpperTabs page scroll partial / source policy tests だけに限定した。XAML、Themes、Settings 画面には触れていない。
- 親レビューでは、Worker Mill の scroll evidence 新旧対応を採用した。`scroll-input` は `ui shell input: operation_reason=scroll` を優先し、既存採取済みログの `page scroll end:` も同じ key として読み続ける。required count は 12 のまま維持する。
- 親レビューでは、Worker Godel の PageUp / PageDown 入力 snapshot ログを採用した。`UpperTabScrollNavigator.TryScrollPage(...)` 成功後だけ `BuildUiShellInputLogMessage("scroll", triggerReason, snapshot)` を `ui-priority` へ出し、既存の `page scroll begin/end/skipped` ログは維持する。
- scroll は user-priority ではなく recent viewport のまま扱う。`BeginUserPriorityWork` は追加せず、Everything poll の catch-up 方針も変えていない。
- 親検証は focused test 75件成功 / 1件skip、Release x64 build 成功、警告0件で完了した。
- 今回も実機同一runの新規採取ではない。次の実機採取では PageUp / PageDown 操作で `ui shell input: operation_reason=scroll trigger_reason=page-up|page-down` と `phase0_log_evidence` の `scroll-input` が揃うかを見る。

### 2.27 2026-06-27 PM親レビュー manual reload入力とScheduler shutdown契約補強

- Worker Darwin / Copernicus / Boole / Pascal は UI簡素化別スレと競合しないよう、MenuActions / Lifecycle / Infrastructure policy / source policy tests に限定した。XAML、Themes、Settings 画面には触れていない。
- 親レビューでは、Worker Darwin の Header Reload 入力 snapshot ログを採用した。`ExecuteHeaderReloadAsync(...)` の開始時に `CaptureUserPriorityOperationSnapshot(IsUserPriorityWorkActive(), isManualMode: true)` を取り、`BuildUiShellInputLogMessage("manual-reload", trigger, inputSnapshot)` を `ui-priority` へ1回だけ出す。既存の full reload、watch suppression、deferred scan、external skin refresh queue の意味は変えていない。
- 親レビューでは、Worker Boole の optional evidence 監査を採用した。manual reload は Phase1 の補助 evidence とし、Phase0 必須12件と `phase0_log_evidence=12/12` の完了条件は増やさない。ログがある時だけ `optional=manual-reload-input` として summary に残す。
- 親レビューでは、Worker Copernicus / Pascal の Scheduler shutdown pending contract 補強を採用した。shutdown pending summary は `scheduler_contract=scheduler-v1` を持ち、pending item は `UiWorkRequestPolicy.BuildRequestSchedulerLogFields(...)` 経由のまま固定する。終了処理を延ばさず、queue drain / release 条件も変えていない。
- 親検証は focused test 40件成功 / 1件skip、Release x64 build 成功、警告0件で完了した。
- 今回も実機同一runの新規採取ではない。次の実機採取では Header Reload 操作で `ui shell input: operation_reason=manual-reload` が optional evidence として出ること、終了時 pending がある場合に `ui work scheduler shutdown pending` summary と item の両方で scheduler 契約を追えることを見る。

### 2.28 2026-06-27 PM親レビュー Image optional evidenceとPersistence状態契約補強

- Worker Bacon / Hubble は UI簡素化別スレと競合しないよう、Infrastructure policy と Persistence / Bookmark 系 tests に限定した。XAML、Themes、Settings 画面には触れていない。
- 親レビューでは、Worker Bacon の画像補助 evidence を採用した。`image-aggregate-decode-plan` は `image_log_reason=image.thumbnail-error-list.aggregate-decode-plan`、`image-stale-discard` は `failure_reason=stale-image-request|stale-player-right-rail` を見る。どちらも optional evidence であり、`image-pipeline` 必須 token と Phase0 必須12件は増やさない。
- 親レビューでは、Worker Hubble の保存ログテスト補強を採用した。bookmark add / delete、score、tag、movie_path、playback stats の hot path 保存結果ログで `persist_contract=persistence-write-v1` と `persist_state=persisted|dirty-retryable` を同時に確認する。保存順、retryable 判定、dirty / failed / notify UI の意味は変えていない。
- 親検証は focused test 40件成功 / 1件skip、Release x64 build 成功、警告0件で完了した。
- 今回も実機同一runの新規採取ではない。次の実機採取では Logタブ preview / live audit summary の `optional=image-aggregate-decode-plan,image-stale-discard` と、保存ログの `persist_state` が `persist_contract` と同じ行で読めることを見る。

### 2.29 2026-06-27 PM親レビュー Worker detail evidenceとCore route詳細契約補強

- Worker Laplace / Singer は UI簡素化別スレと競合しないよう、Infrastructure policy と core route source policy tests に限定した。XAML、Themes、Settings 画面には触れていない。
- 親レビューでは、Worker Laplace の Worker detail optional evidence を採用した。`worker-diagnostic-context`、`worker-capability-count`、`worker-metric-count` はそれぞれ `diagnostic_context_count` / `capability_count` / `metric_count` を見る。いずれも optional evidence であり、`worker` 必須 token と Phase0 必須12件は増やさない。
- 親レビューでは、Worker Singer の core route 詳細ログ契約補強を採用した。skin refresh は `operation_reason=skin.host-refresh` と `definition_mode`、Player は `player_surface_ready` と `player_transition=start|stop`、Watcher は `watch_apply_kind` / `watch_reason` / `operation_reason` を戻せないようにした。実行順や same-document skip、Player制御、watch query-only / full fallback 判定は変えていない。
- 親側で、外部skinヘッダー source policy の旧 48px / 170px / 420px 期待値を、UI簡素化後の 32px / 132px / 320px 契約へ更新した。これはテスト整合だけで、XAML本体は変えていない。
- 親検証は focused test 170件成功 / 1件skip、Release x64 build 成功、警告0件で完了した。
- 今回も実機同一runの新規採取ではない。次の実機採取では Worker detail optional evidence と skin / Player / Watcher core route 詳細が Logタブ preview / live audit summary と実ログ行で読めることを見る。

### 2.30 2026-06-27 PM親レビュー ReadModel Diff evidenceとScheduler詳細契約補強

- Worker Fermat / Sagan は UI簡素化別スレと競合しないよう、Phase0 evidence policy と Scheduler policy / runtime tests に限定した。XAML、Themes、Settings、実行順へは触れていない。
- 親レビューでは、Worker Fermat の ReadModel Diff detail optional evidence を採用した。`readmodel-diff-single` は `diff_change_set=single`、`readmodel-diff-total` は `diff_changed_total=` を見る。どちらも optional evidence であり、`readmodel-diff` contract evidence と Phase0 必須12件は増やさない。
- 親レビューでは、Worker Sagan の Scheduler 詳細ログ契約補強を採用した。admission は `accepted` / `target_index`、take は `sequence` / `has_request` / `pending_count_after`、timeout release は `timeout_released` / `sequence` / `pending_count_after` を helper 経由で出すことを固定した。queue 判定、timeout / drain 条件、UI 実行順は変えていない。
- 親検証は focused test 51件成功 / 1件skip、Release x64 build 成功、警告0件で完了した。
- 今回も実機同一runの新規採取ではない。次の実機採取では watch 1件追加 / rename の `diff_change_set=single` / `diff_changed_total=1` と、Scheduler admission / take / timeout / shutdown pending の詳細 fields が Logタブ preview / live audit summary と実ログ行で読めることを見る。

### 2.31 2026-06-27 PM親レビュー Image detailとPersistence状態語彙補強

- Worker Rawls / Banach は UI簡素化別スレと競合しないよう、tests に限定した。XAML、Themes、Settings、production 挙動には触れていない。
- 親レビューでは、Worker Rawls の画像詳細ログ契約補強を採用した。`image_contract=image-pipeline-v1`、`image_key`、`visible_priority`、`image_cache_policy`、`should_decode`、`image_result_revision`、`resolved`、`placeholder`、`stale`、`failure_reason`、aggregate decode plan sample、stale discard reason を focused tests で固定した。
- 親レビューでは、Worker Banach の保存状態語彙補強を採用した。application settings / bookmark / hot path / skin profile の保存ログで `persist_contract=persistence-write-v1`、`write_succeeded=true|false`、`persist_state=persisted|dirty-retryable|failed-notify|failed`、`dirty` / `failed` / `retryable` / `notify_ui` を同じ契約語彙として確認する。
- 親検証は focused test 76件成功、Release x64 build 成功、警告0件で完了した。
- 今回も実機同一runの新規採取ではない。次の実機採取では image aggregate / stale discard と persistence state fields が `debug-runtime.log` 上で同じ contract 行として読めることを見る。

### 2.32 2026-06-27 PM親レビュー Phase0 optional evidenceとWorker failure語彙補強

- Worker Faraday(Phase0) / Parfit は UI簡素化別スレと競合しないよう、Infrastructure policy、Thumbnail worker JSON helper、対応 tests に限定した。XAML、Themes、Settings、Views/Main の UI 本体には触れていない。
- 親レビューでは、Worker Faraday(Phase0) の Phase0 optional evidence 補強を採用した。ReadModel Diff は `diff_source_revision=` / `diff_view_revision=` / `diff_full_fallback_reason=`、Scheduler は `accepted=` / `target_index=` / `has_request=` / `timeout_released=` / `pending_count_after=` を補助 evidence として拾う。Scheduler detail は `scheduler_contract=scheduler-v1` と同じ行にある時だけ採用し、Phase0 必須12件は増やさない。
- 親レビューでは、Worker Parfit の Worker契約 failure / skip / result 語彙補強を採用した。queue / metadata probe / rescue worker result で `worker_status` または `status`、`worker_stage`、`retryability`、`retryable`、`failure_kind`、`failure_reason`、`metric_count`、`capability_count`、`diagnostic_context_count` を focused tests で固定した。rescue worker result ログには `worker_stage=completed` / `retryable` / `failure_kind` を追加したが、queue 実行順、worker 起動順、IPC方式、外部プロセス構造は変えていない。
- 親検証は focused test 94件成功、Release x64 build 成功、警告0件で完了した。
- 今回も実機同一runの新規採取ではない。次の実機採取では Phase0 summary の optional に ReadModel revision / fallback と Scheduler detail が出ること、Worker failure / skip / result 行で failure / retry / count 語彙が同じ契約行として読めることを見る。

### 2.33 2026-06-27 PM親レビュー Refresh許容線とPhase7 detail evidence補強

- Worker Schrodinger / Halley は UI簡素化別スレと競合しないよう、source policy tests と Phase0 evidence policy / tests に限定した。production UI、XAML、Themes、Settings、runtime 挙動は触っていない。
- 親レビューでは、Worker Schrodinger の Refresh 許容線 source policy 補強を採用した。WPF本体C#全体で `Items.Refresh()` を禁止し、直書き `Refresh();` は `startup-first-page-detail-sync` と `collection-apply-selection-changed-compat` の2箇所、`FilterAndSort(..., true)` は `startup-fallback-full-reload` と `startup-partial-sort-full-order` の2箇所だけに固定する。
- 親レビューでは、Worker Halley の Phase7 core route detail optional evidence を採用した。skin は `operation_reason=skin.host-refresh` / `definition_mode=`、Player は `player_surface_ready=` / `player_transition=`、Watcher は `watch_apply_kind=` / `watch_reason=` を、対応する `core_route` と同じ行にある時だけ拾う。Phase0 必須12件は増やさず、optional count は 28 として扱う。
- 親検証は focused test 81件成功、Release x64 build 成功、警告0件で完了した。
- 今回も実機同一runの新規採取ではない。次の実機採取では Phase0 summary の optional に Phase7 detail が出ることと、Refresh / Items.Refresh / `FilterAndSort(..., true)` の許容線が増えていないことを source policy で守り続ける。

## 3. Roadmap

### Phase 0. 現状固定とログ証跡補強

- 既存の `UiOperationPriorityPolicy`、ReadModel builder、partial分離、source policy を正本として固定する。
- 検索、sort、scroll、Player、watch、thumbnail、skin を同じ Release run で操作し、`debug-runtime.log` だけで割り込み、延期、fallback、apply 時間を説明できるようにする。
- `Refresh()` / `Items.Refresh()` / `FilterAndSort(..., true)` の許容線は増やさない。
- 済: UI Shell / ReadModel Diff / Scheduler / Image / Persistence / Worker / Skin / Player / Watcher の contract 識別子は source policy で固定した。これは実機ログ確認の代替ではなく、各 Phase のログ契約を戻さないための土台とする。
- 済: `DebugRuntimeLogRunSlicePolicy` と `DebugRuntimeLogEvidencePolicy` で、複数起動分が連結された `debug-runtime.log` から最新runを切り出し、必要な evidence token の欠落を確認できる足場を追加した。これは採取後の確認 helper であり、実機ログ採取の代替ではない。
- 済: 最新runの `log_run_lines=...` 要約と、Phase0 操作evidenceの `phase0_log_evidence=...` 集計を追加した。実機ログ採取後はこの2つを使い、同一run内で startup / search / sort / Player / watch / image / persistence / worker / skin の抜け漏れを確認する。
- 済: Phase0 操作evidence は scroll / thumbnail も含めた 12 token へ広げ、Logタブ preview で `log_run_lines` / `log_evidence` / `phase0_log_evidence` を先頭表示できるようにした。
- 済: `DebugRuntimeLogAuditSummaryPolicy` で最新run、run時間窓、contract evidence、Phase0 操作evidence、次アクションの5行summaryを合成し、Logタブ preview もこの policy を使うようにした。UIスレッドのファイルI/Oは増やさず、既存の背景読み込み境界を維持する。
- 済: search / sort 入力入口が `BuildUiShellInputLogMessage(...)` 経由で `ui shell input` を出すことを source policy で固定し、`IMM_PHASE0_LOG_AUDIT_LIVE=1` の live audit test で採取済み `debug-runtime.log` を同じ summary で検証できるようにした。これは実機採取の代替ではなく、採取後の不足key確認導線とする。
- 済: live audit は contract evidence と Phase0 操作evidence の両方を完了条件にし、opt-in / 任意ログパス / `LOCALAPPDATA` 既定 / 共有読み取り / 失敗summaryを source policy で固定した。
- 済: audit summary は `phase0_audit_complete=true|false` を出す。判定は contract evidence と Phase0 操作evidence の両方が揃った時だけ true とし、次アクションが none でも contract 欠落がある時は false のままにする。
- 済: scroll evidence は `ui shell input: operation_reason=scroll` と旧 `page scroll end:` の両方を `scroll-input` として認識する。新旧語彙の過渡互換であり、required count は増やさない。
- 済: manual reload / ReadModel Diff detail / image aggregate-decode-plan / image stale discard は optional evidence として summary に残す。採取時の補助情報であり、Phase0 必須12件の完了条件は増やさない。
- 済: Worker DTO detail の diagnostic context / capability / metric count は optional evidence として summary に残す。採取後に Worker detail の不足を見落としにくくするための補助であり、Phase0 必須12件の完了条件は増やさない。
- 済: ReadModel Diff の source / view revision と fallback reason、Scheduler の accepted / target_index / has_request / timeout_released / pending_count_after は optional evidence として summary に残す。採取後の Phase2 / Phase3 詳細不足を見落としにくくする補助であり、Phase0 必須12件の完了条件は増やさない。
- 済: Phase7 core route detail の skin operation / definition mode、Player surface ready / transition、Watcher apply kind / reason は optional evidence として summary に残す。`core_route` と同じ行にある時だけ採用し、Phase0 必須12件は増やさない。
- 済: `Items.Refresh()` は WPF本体C# production code 全体で戻さない。直書き `Refresh();` と `FilterAndSort(..., true)` は既存の許容2箇所だけを source policy で固定し、通常経路の全面再評価逆流を検出する。
- 未: 2026-06-27 の実ログ audit は `log_evidence=2/9`、`phase0_log_evidence=1/12` で不足を示した。次は同一 Release run で startup / search / sort / scroll / Player / watch / image / thumbnail / skin を操作し、summary 欠落が消えることを確認する。

### Phase 1. UI Shell 入力契約

- UI event handler は、入力、選択、スクロール、表示範囲、操作優先度の snapshot を作るだけへ寄せる。
- `UiOperationPriorityPolicy` を起点に、search / sort / player / viewport / manual reload の操作状態を共通 snapshot として扱う。
- user-priority begin / end は `UiOperationSnapshot` fields をログへ出す。次は search / sort / player の各入口で同じ fields が実機ログへ出ることを確認する。
- 済: search / sort 入力入口ログは `ui shell input` と `operation_reason` / `trigger_reason` / `UiOperationSnapshot` fields を持ち、user-priority begin 前の入口状態を同じ語彙で読める。
- 済: UI Shell snapshot fields は `ui_shell_contract=ui-shell-v1` を持ち、user-priority begin / end、search / sort 入力入口を同じ契約識別子で追える。
- 済: Player 再生状態の実遷移時も `ui shell input` と `operation_reason=player-playback` / `trigger_reason` / `UiOperationSnapshot` fields を `ui-priority` へ出す。同状態通知では出さず、Player 操作ログ量を増やしすぎない。
- 済: PageUp / PageDown の実スクロール成功時も `ui shell input` と `operation_reason=scroll` / `trigger_reason=page-up|page-down` / `UiOperationSnapshot` fields を `ui-priority` へ出す。scroll は user-priority にせず recent viewport のまま扱う。
- 済: Header Reload 明示手動再読込入口も `ui shell input` と `operation_reason=manual-reload` / `trigger_reason` / `is_manual_mode=true` / `UiOperationSnapshot` fields を `ui-priority` へ出す。Phase0 監査では必須 token を増やさず optional evidence として扱う。
- Scheduler本体はまだ作らず、既存の user-priority / watch suppression / Everything poll delay の判断口を揃える。

### Phase 2. ReadModel Store と Diff-first

- `MovieRecords` の互換表現と UI表示用 ReadModel を段階的に分ける。
- 小変更は追加、削除、更新、移動の diff apply を通常経路にする。
- query変更、sort key変更、DB切替、大量変更、unsafe dirty だけ full fallback とし、`fallback_reason` をログへ残す。
- 2026-06-19 時点では、`MovieViewDiffApplyPolicy` による判定語彙固定に加え、同一 stable key update、小さな insert / remove、sort-only stable key Move + Replace の実 apply を局所適用へ進めた。大量変更や unsafe dirty はまだ保守的に扱い、実機ログを見て次段で進める。
- watch apply request は source / applied の changed path 数と `diff_change_set` を出す。次は watch 1件追加 / rename が `single` のまま diff-apply 候補で流れるかを実機ログで確認する。
- 済: diff apply plan fields は `diff_contract=readmodel-diff-v1` を持ち、ReadModel apply と Watch UI apply request の差分契約ログを同じ識別子で追える。
- 済: diff ログは `diff_changed_total` を持ち、added / deleted / updated / moved の合計規模を同じ行で読める。
- 済: watch apply request の change set ログも `diff_changed_total` を持ち、full fallback 時も source 側の変更総量を同じ行で読める。
- 済: `diff_change_set=single` / `diff_changed_total=` は Phase0 optional evidence として audit summary に残り、次回採取で watch 小変更の detail 欠落を見落としにくくした。
- 済: `Items.Refresh()` の WPF本体C#全体禁止、直書き `Refresh();` の2箇所許容、`FilterAndSort(..., true)` の2箇所許容を source policy で固定した。これは挙動変更ではなく、ReadModel / Diff-first へ寄せた許容線の後退検出である。
- `MainVM.ReplaceFilteredMovieRecs(...)` は段階的に diff apply へ置き換えるが、互換 fallback と source policy を残す。

### Phase 3. In-process Scheduler

- bounded queue、coalesce、latest-only、priority、release reason、timeout log、shutdown bounded drain を持つ小さな in-process scheduler を導入する。
- 優先順は、入力、選択、スクロール、Player、visible画像、最新検索 / sort、watch小差分、thumbnail / rescue、skin catalog の順にする。
- 最初は watch / poll / thumbnail refresh 予約だけを載せ、全機能を一気に移さない。
- 現在は実行器本体へ進む前段として、既存予約のログ語彙を `UiWorkRequestPolicy` の helper へ寄せ、さらに `UiWorkSchedulerPolicy` で入場・置換・timeout の純粋判断を固定した段階。`timeout_policy=none` は明示的な timeout drain 未導入を示し、実機ログで必要性が見えた時だけ timeout budget を持たせる。
- external skin host refresh queue も scheduler 語彙へ接続済み。ただし interval、freshness、Header Reload / fallback retry の意味は変えていない。
- 済: Scheduler admission / take ログは `accepted` / `target_index` / `has_request` を持ち、入場 / 置換 / 拒否 / 取り出しの判定結果を同じ行で読める。
- 済: Scheduler timeout ログは `timeout_released` を持ち、timeout 判定結果が解放へ進んだかを同じ行で読める。
- 済: Scheduler timeout release ログは `sequence` / `pending_count_after` を持ち、timeout で落ちた pending と残件数を同じ行で読める。
- 済: Scheduler admission / take / timeout ログは `scheduler_contract=scheduler-v1` を持ち、入場、実行引き渡し、timeout release を同じ契約識別子で追える。
- 済: Scheduler shutdown pending summary も `scheduler_contract=scheduler-v1` を持つ。pending item は `BuildRequestSchedulerLogFields(...)` 経由のまま維持し、終了時に残った request の release reason / bounded drain / coalesce / latest-only を同じ語彙で追える。
- 済: Scheduler admission / take / timeout release の詳細 fields は source policy と runtime test で固定し、`accepted` / `target_index` / `has_request` / `timeout_released` / `sequence` / `pending_count_after` を戻せないようにした。

### Phase 4. Image Pipeline 統一

- 上側タブ、下側 ERROR / 進捗、詳細、Player右レールの画像要求を visible-first に寄せる。
- 画像存在確認、stamp取得、decode、ERROR marker判定は UI スレッドから外す。
- cache miss は placeholder で先に返し、成功 / missing / canceled / failed を revision 付きで戻す。
- 現在は下側 ThumbnailError 一覧の preview fallback と画像状態集約を `ImageRequest` / `ImageLoadResult` / `ImageDecodePlanResult` 語彙へ寄せた段階。次は実機ログで stale discard と aggregate / aggregate-decode-plan を確認する。
- Player右レールは同期decodeのまま、`ImageDecodeResult` と stale canceled 語彙を保持する。次は実機ログで stale discard の発生有無を確認する。
- 済: Image decode plan ログは `image_result_revision` / `resolved` / `placeholder` / `stale` / `failure_reason` を持ち、背景 probe の判定結果を decode 計画語彙と同じ行で読める。
- 済: Image load / decode ログは `visible_priority` / `image_cache_policy` / `should_decode` を持ち、visible-first 要求文脈と decode 対象判定を同じ行で読める。
- 済: Image load / decode / decode plan ログは `image_key` を持ち、実パスを増やさず対象キーを同じ行で追える。
- 済: Image load / decode / decode plan ログは `image_contract=image-pipeline-v1` を持ち、画像 pipeline の各結果ログを同じ契約識別子で追える。
- 済: Phase0 audit summary は aggregate-decode-plan と stale discard を optional evidence として残す。実機ログ採取後に画像 pipeline の観測不足を見落としにくくするための補助であり、完了判定そのものは実ログで閉じる。
- 済: Image load / decode / aggregate-decode-plan / stale discard の詳細 fields は focused tests で固定し、visible-first 文脈、対象キー、結果 revision、placeholder / stale / failure reason を同じ画像契約として戻せないようにした。

### Phase 5. Persistence Pipeline

- 設定保存、score、view_count、tag、bookmark、skin profile、movie_path 更新を同じ背景保存方針へ寄せる。
- UI は表示値を先に反映し、保存は直列 background queue へ送る。
- 失敗時だけ dirty / failed / retryable をログと最小UI通知で扱う。
- application settings / player volume は保存成功時も共通 `PersistenceWriteResult` 語彙で完了を読める。さらに保存成功ログにも `dirty=false failed=false retryable=false notify_ui=false` を出すため、実機ログでは `write_succeeded=true/false` と状態語彙を同じ行で見る。成功ログ量が多すぎる場合だけ、理由別に絞る。
- 済: 保存結果ログは `persist_state=persisted|dirty-retryable|failed-notify|failed` を持ち、成功、retryable dirty、UI通知候補失敗を同じ行で読める。
- 済: Persistence write 共通ログは `persist_contract=persistence-write-v1` を持ち、settings / bookmark / skin profile など保存系ログを契約単位で追える。
- 済: bookmark add / delete、score、tag、movie_path、playback stats の hot path 保存結果ログは、`persist_contract` と `persist_state` を同じ結果ログで確認するテストを持つ。production の保存順や失敗状態の意味は変えない。
- 済: 保存結果ログの `persist_contract` / `write_succeeded` / `persist_state` / `dirty` / `failed` / `retryable` / `notify_ui` は focused tests で固定し、成功 / retryable dirty / UI通知候補失敗を同じ保存契約で読めるようにした。

### Phase 6. Worker 契約

- thumbnail / rescue / metadata probe を、UI非依存の request / result / progress / artifact 契約へ揃える。
- 既存の `ThumbnailIpcDtos`、rescue worker job json、thumbnail queue runtime を土台に、まず in-process adapter で契約を固定する。
- 済: watch metadata probe は `WatchMetadataProbeWorkerContractAdapter` で request / result / artifact / metrics へ写せる入口を追加した。runtime 挙動変更や IPC 接続はまだ行わない。
- 済: thumbnail queue の結果ログは一部 `WorkerJobResultDto` fields を併記する。成功全件ログ化はせず、既存の結果系ログだけに留める。
- 済: rescue worker の job/result JSON 経路も既存ログへ Worker DTO fields を併記する。schema変更や IPC 導入は行わない。
- 済: thumbnail queue の failure / skip 系ログは `BuildWorkerQueueLogFields(...)` で request / progress / result の代表 fields を同じ行へ畳む。通常成功全件ログ化はしない。
- 済: Thumbnail Queue の request / queue 代表ログは `capability_count` と `diagnostic_context_count` を持ち、入力数以外の契約規模も同じ行で読める。DTO schema と実行順は変えない。
- 済: watch metadata probe の request / probe 統合ログは `diagnostic_context_count` を持ち、入力数 / capability 数と診断文脈の規模を同じ行で読める。
- 済: rescue worker request ログは `diagnostic_context_count` を持ち、thumbnail queue / watch metadata probe と同じ診断文脈規模で読める。
- 済: rescue worker / watch metadata probe の helper fields は input_count / capability_count / 診断文脈 / result metrics を含む。実行方式、IPC方式、DB更新順は変えない。
- 済: thumbnail queue / rescue worker / watch metadata probe の result 系ログは `metric_count` を持ち、metrics が空か、どの程度付与されているかを結果行だけで読める。
- 済: Thumbnail Queue / Rescue Worker / Watch metadata probe の Worker契約ログは `worker_contract=worker-job-v1` を持ち、request / progress / result / combined logs を同じ契約識別子で追える。
- 済: Phase0 audit summary は `diagnostic_context_count` / `capability_count` / `metric_count` を optional evidence として残す。実機ログ採取後に Worker DTO detail が支配要因確認に足りるかを見る補助であり、完了判定そのものは実ログで閉じる。
- 済: Worker failure / skip / result 系の代表語彙は `worker_status` または `status`、`worker_stage`、`retryability`、`retryable`、`failure_kind`、`failure_reason`、`metric_count`、`capability_count`、`diagnostic_context_count` を focused tests で固定した。rescue worker result ログにも `worker_stage=completed` / `retryable` / `failure_kind` を出し、実機ログで失敗種別と再試行性を同じ契約行から読めるようにした。
- sidecar / IPC は、契約が固定され、実機ログで UI 詰まりの支配要因が worker 境界にあると確認できた後だけ導入する。

### Phase 7. Skin / Player / Watcher の Core 接続

- skin refresh、Player操作、Watcher change set は UI へ直接押し込まず、Scheduler / ReadModel / Persistence 経由へ寄せる。
- skin は catalog / persist / navigate / stale を分ける。
- Player は surface操作と保存を分ける。
- Watcher は change set 正規化に専念し、UI apply の実行者にしない。
- 済: 外部 skin refresh ログは queue / deferred / rejected / batch flush / begin で `core_route=skin-refresh`、`operation_reason=skin.host-refresh`、`refresh_reason`、`request_trace`、`definition_mode` を読める。
- 済: Player 再生状態ログは状態遷移時だけ `core_route=player-playback`、`player_surface`、`player_surface_ready`、`active`、`player_transition=start|stop`、`operation_reason`、`reason` を読める。
- 済: Player 再生状態の実遷移は、同じタイミングで UI Shell input snapshot と Player core route を出す。これにより実機ログで「Player 操作入口の UI 状態」と「Player surface / transition」を隣接して確認できる。
- 済: Watcher apply request ログは `core_route=watch-ui-apply`、`watch_apply_kind`、`watch_reason`、`operation_reason` を持ち、change set から UI apply へ渡る境界を同じ core route 語彙で読める。
- 済: skin / Player / Watcher の core route helper は詳細 fields の契約値を focused tests で固定した。実行順、refresh freshness、same-document skip、Player surface 制御、watch fallback 判定は変えない。
- 済: skin / Player / Watcher の core route detail は Phase0 optional evidence にも出る。skin は `operation_reason=skin.host-refresh` / `definition_mode=`、Player は `player_surface_ready=` / `player_transition=`、Watcher は `watch_apply_kind=` / `watch_reason=` を同じ route 行で読める時だけ補助 evidence とする。

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
