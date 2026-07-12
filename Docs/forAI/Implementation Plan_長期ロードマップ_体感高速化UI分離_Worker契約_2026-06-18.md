# Implementation Plan 長期ロードマップ 体感高速化 UI分離 Worker契約 2026-06-18

初版: 2026-06-18

再構築: 2026-07-11

親レビュー: 2026-07-12

位置づけ: UIのスムーズ化とユーザーストレス最小化を長期判断へ落とす実装正本

変更概要:

- 実装量を表す総合進捗率を廃止し、ユーザー体感、実装接続、実機証跡の3条件で完了を判定する構成へ変更した。
- 2026-06-25から2026-06-27までの逐次レビュー記録を、現行契約と到達点へ集約した。詳細履歴はGit履歴を正本とする。
- 「速い」だけでなく、入力を失わない、表示を飛ばさない、古い結果を見せない、背後処理が操作へ譲ることを固定原則にした。
- Phase 0からPhase 7を、操作シナリオ、成果物、完了ゲート、次フェーズへの接続が読める形へ再構築した。
- 直近の最優先を、同一Release runの実機証跡採取と、検索・sort・scroll・watch小差分の体感ボトルネック特定に固定した。
- 主要8シナリオのlog evidenceと目視確認を分離したscorecardを追加し、scorecardだけではPhase 0を完了扱いにしない判断を固定した。
- 主要8シナリオ36項目の目視確認をJSONへ記録し、log evidenceと合わせて監査できる診断導線を追加した。
- 目視確認JSONを診断起動時のsession IDと開始ローカル時刻へ一度だけ束縛し、最新ログrunの開始時刻とsequence=1を照合して同一runを機械判定するようにした。
- 最新runからUI停止delayのp50 / p95 / max、最大queue深さ、stale破棄行数、full fallback行数を要約できるようにした。runtimeログ契約は増やしていない。
- 通常UIと外部skinのインメモリsort後に先頭選択へ戻していた後処理を除き、既存の差分Moveが選択とscrollを維持できる経路を塞がないようにした。起動partialの全件復旧だけは従来互換を維持する。
- 検索結果更新後と上側タブ往復時も、既存選択が残る時は維持し、未選択時だけ先頭へ戻すようにした。
- Reset更新では、apply前の主選択1件をID優先・path fallbackのstable keyで捕捉し、apply後の現行レコードへ復元するようにした。
- Reset更新の複数選択も、主選択を先頭にしたstable key一覧として捕捉し、曖昧な重複keyを除いて現行レコードへ復元するようにした。
- Reset更新のscrollも、先頭可視項目のstable keyとviewport内top offsetを捕捉し、同じ項目が残る時だけReset後の表示位置を補正するようにした。
- Reset更新のkeyboard focusも、直前に現在一覧内へfocusがあった場合だけstable keyで捕捉し、同一タブの実現済みcontainerへ戻すようにした。
- 検索・sort・Player準備が250 msを超えた時だけ、操作を無効化せずヘッダーへ進行中表示を出し、短時間完了と後着tickでは表示しないようにした。
- 2026-07-12のコピーDB実機確認で、startup partial中の検索が約4.3万件のsourceを毎回再構築し、約7から8秒を要することを特定した。現行revisionの全件再構築成功時だけsource completeへ遷移し、次回以降をquery-only経路へ戻した。
- 同じrunでサムネイル進捗の同期UI反映が連続し、TextBox入力中に最大1.25秒のUI停止を確認した。検索入力開始から検索実行までuser-priorityを維持し、サムネイル進捗反映は受付時と実行直前の双方で遅延してlatest-onlyで再開する契約へ変更した。
- 2026-07-12の更新版Release runでは、startup partial中も検索可否と入力優先を分離し、最後の入力からdebounce満了まで `search-input` user-priorityを維持した。入力中のサムネイル進捗反映は全件deferredとなり、同区間のUI停止ログは出ていない。
- サムネイル進捗の実UI applyは直近0.1から15 msで、直後の計測CSV同期I/Oが長いUI占有の主因だった。計測保存を容量1のlatest-only背景処理へ移し、診断処理がUI待ちや無制限queueを作らないようにした。
- 同runの検索は、初回全件構築が `total_ms=7144 source_apply_ms=6234`、次回が `route=query-only total_ms=88 source_apply_ms=0` となり、全件source完成後の再利用を実機ログで確認した。
- Phase 0 live監査は、監査開始時の実機ログを一時snapshotへ固定し、子テスト自身のruntime logを別の一時sinkへ分離した。監査中の本体追記や子プロセスが判定対象runを汚さず、終了時に環境変数と一時ファイルを必ず戻す契約とした。
- 実機ログのsequence採番とfile appendを同じlock内へ置き、並列追記でもfile上の連番を単調化した。run sliceは後続の `sequence=1` だけを新run境界として扱い、過去ログの軽微な逆転や重複を誤ってrun開始にしない。
- 2026-07-12の分離後Release監査では `log_run_lines=1084 sequence=1-1084` でsession境界を通過した。失敗理由は `missing-contract-evidence` のみとなり、startupとskinはログ証拠完了、残りはsearch / sort / scroll / player / watch / image / persistence / thumbnailの操作採取である。
- 同一runへsearch / sort / scrollの実UI入力を追加し、3操作のログ証拠を完了した。監査は `phase0_log_evidence=8/12` まで進み、次の不足はplayer / watch / image / persistenceである。
- このrunのUI停止はp95 1249 ms、最大1495 msで、activity内訳はThumbnail 70件、Watch 4件だった。user-priority中もサムネイルconsumerが8件ずつ新規leaseを取得していたため、表示反映だけでなく新規lease取得も入力へ譲ることを次の支配要因とした。
- サムネイルqueueへuser-priority lease gateを追加した。取得済みjobは中断せず、新規取得と補充だけを延期し、解除後に自動再開する。更新版Releaseでは入力開始23 ms後にdeferし、2817 msの入力優先区間内は新規lease 0件、解除後にresumeしたことを確認した。
- Phase 0診断runはサムネイル大量ログで数分以内に既定20MBへ達し、同一run前半のstartup証拠がrotation先へ退避されることを実機で確認した。通常起動の20MBは維持し、診断プロセスだけ128MBへ拡張した。
- live監査はManualReview session開始後に更新された同basenameのrotation片と現行logを時系列snapshotへ連結する。既存rotation済みrunで `log_run_lines=51675 sequence=1-51699` を復元し、startup / watch小差分 / skin / persistenceの証拠が再び完了扱いになることを確認した。
- ユーザー実機報告で、プレーヤータブ右レールのサムネイルスクロールが遅いことを最優先へ上げた。PlayerThumbnailListは仮想化済みだったが、Pixel scrollと先行cache範囲に加え、container実現時の画像同期decodeがスクロール中のUIを最大560 ms占有していた。
- PlayerThumbnailListをItem scroll、半ページcacheへ絞り、wheel / PageUp / PageDownの描画前から250 msのuser-priorityを維持する。さらに右レール画像はUI上でmemory cache hitだけを返し、missは容量64・重複排除・single-flightの背景warmへ送り、scroll idle後にvisible項目だけlatest-onlyで再評価する。
- 更新版Releaseの8回PageDownは初回63 ms、以降30から32 msで、スクロールuser-priority中のUI停止ログは0件だった。新規thumbnail leaseも開始12 ms後にdeferし、idle後にresumeした。完了判定はユーザーのホイール実操作による目視・体感確認で閉じる。

## 0. 結論

IndigoMovieManagerの長期目標は、処理時間の数字だけを縮めることではない。

ユーザーが操作した瞬間に反応が返り、背後処理が動いていても一覧、選択、スクロール、タブ、Playerが落ち着いて使え、待ちが発生しても理由と進行が分かり、操作結果が後着処理で巻き戻らない状態を作る。

本ロードマップでは、これを「ストレスなし操作」と呼ぶ。

当面の技術方針は次で固定する。

1. WPF一覧を維持し、内部を `UI Shell`、`ReadModel`、`Scheduler`、`Image Pipeline`、`Persistence Pipeline`、`Worker契約` へ段階分離する。
2. UIスレッドは入力、軽量snapshot、最小diff apply、描画だけを担当する。
3. 検索、sort、scroll、選択、タブ切り替え、Player操作を背後処理より優先する。
4. 全面再評価を通常経路にしない。小変更はdiff、古い結果はrevisionで破棄する。
5. 実装済みでも、同一Release runの実機ログと実表示で閉じていないものは完了扱いにしない。
6. WebView2一覧化、`.wb`変更、`MainWindow`全面置換、IPC / sidecar先行導入は行わない。

## 1. ユーザー体感の定義

### 1.1 守る4原則

#### 原則A. 入力はすぐ受け取る

- クリック、キー入力、選択、スクロール、タブ切り替えを同期DB、file I/O、画像decodeで待たせない。
- 完了に時間がかかる処理でも、入力を受理したことは先に画面へ返す。
- 同じ操作の連打や連続入力は、古い要求を積まずlatest-onlyまたはcoalesceで扱う。

#### 原則B. 表示の連続性を守る

- 一覧更新で選択、フォーカス、スクロール位置を不必要に失わない。
- 読み込み途中に、意味のないblank、全件消去、ちらつきを見せない。
- 画像未準備時はplaceholderを維持し、readyになった対象だけを差し替える。

#### 原則C. 最新の意思だけを反映する

- 検索、sort、DB切り替え、画像要求、skin refreshはrevisionを持つ。
- cancelできない処理でも、後着した古い結果はUI apply前に破棄する。
- 古い結果で選択、一覧、skin、Player表示を巻き戻さない。

#### 原則D. 背後処理はユーザーへ譲る

- watch、poll、thumbnail、rescue、catalog、保存、prewarmは入力中に割り込まない。
- 背後処理は無制限に停止せず、bounded queue、延期、間引き、再開でstarvationを防ぐ。
- shutdownは `受付停止 -> complete -> bounded drain -> timeout記録` の順で閉じる。

### 1.2 ストレスとして扱う現象

次のいずれかが起きた場合、単体処理が高速でも未完了とする。

- 入力したのに反応がなく、受理されたか分からない。
- 検索やsortの途中でスクロール、選択、Player操作が固まる。
- 一覧更新で選択、フォーカス、スクロール位置が飛ぶ。
- 古い検索、watch、画像、skin結果が後から表示を巻き戻す。
- 画像、skin、Playerが一度blankになり、不要な再読込を繰り返す。
- 保存、watch、thumbnail進捗のためにUIが待つ。
- 終了時に固まる、または保存の成否が分からない。
- エラー回復が通常操作より優先され、通常動画のテンポを壊す。

### 1.3 体感予算

以下は初期予算であり、Phase 0の実測後にDB規模別の基準へ調整する。数字だけを満たして表示を壊す変更は採用しない。

| 操作 | 初期予算 | 守る体感 |
|---|---:|---|
| クリック、選択、タブ切り替え | 100 ms以内に視覚応答 | 入力受理が分かる |
| 検索、sort開始 | 100 ms以内に状態反映 | 入力欄や選択が固まらない |
| 検索、sortの最初の有用表示 | 300 msを目標 | 大件数時も古い結果を先に捨てる |
| scroll、PageUp、PageDown | 連続操作中に長い停止を作らない | visible範囲を最優先する |
| 250 msを超える明示操作 | 進行中または継続利用可能と分かる | 無反応に見せない |
| shutdown | bounded drain内で終了 | timeout時も理由をログへ残す |

Phase 0では、各予算の開始点、終了点、視覚応答点をログ名と操作手順で固定する。測れない予算は完了判定に使わない。

## 2. 現在位置

### 2.1 到達点

| 領域 | 現在の到達点 | 判定 |
|---|---|---|
| UI Shell | `UiOperationSnapshot` と `ui_shell_contract=ui-shell-v1` があり、search / sort / scroll / Player / manual reload / tab switchを同じ語彙で観測できる | 接続中 |
| ReadModel / Diff | stable key更新、小規模insert / remove、sort-only Move + Replace、watch change set契約がある | 接続中 |
| Scheduler | bounded、coalesce、latest-only、priority、timeout判断を持つ最小runtimeを一部経路へ接続済み | 接続中 |
| Image | `ImageRequest`、load / decode result、visible priority、stale discardの契約がある | 接続中 |
| Persistence | settings、Player、bookmark、score、tag、movie path、skin profileなどを共通保存語彙へ寄せた | 接続中 |
| Worker | thumbnail、rescue、metadata probeをrequest / progress / result / artifact DTOへ写す入口がある | 契約済み |
| Skin / Player / Watcher | `core_route` と詳細fieldで実行境界を観測できる | 接続中 |
| 実機証跡 | focused testとsource policyは厚いが、主要操作を同一Release runで閉じていない | 実機確認待ち |

### 2.2 維持する契約識別子

- UI Shell: `ui_shell_contract=ui-shell-v1`
- ReadModel Diff: `diff_contract=readmodel-diff-v1`
- Scheduler: `scheduler_contract=scheduler-v1`
- Image: `image_contract=image-pipeline-v1`
- Persistence: `persist_contract=persistence-write-v1`
- Worker: `worker_contract=worker-job-v1`
- Skin: `core_route=skin-refresh`
- Player: `core_route=player-playback`
- Watcher UI apply: `core_route=watch-ui-apply`

### 2.3 現在の最大リスク

1. 契約とログの整備量に対し、実操作で本当に滑らかかの証跡が不足している。
2. 部分接続のSchedulerやPipelineがあり、機能ごとに優先制御が分散している。
3. full fallbackの許容線は固定したが、大件数sort、watch小差分、画像staleの実頻度が十分に見えていない。
4. 選択、フォーカス、スクロール位置の連続性は、性能ログだけでは保証できない。
5. 個別の小口補強を積み続けると、ユーザー体感を変えないログ作業が主目的化する。

したがって、次は新しい契約語彙を増やす段階ではない。既存契約を実シナリオで横断し、支配要因を3件以内へ絞ってから実装する。

## 3. 目標アーキテクチャ

### 3.1 責務境界

| 境界 | 持つ責務 | 持たない責務 |
|---|---|---|
| UI Shell | 入力受付、軽量snapshot、描画、最小diff apply、表示中範囲通知 | DB全件読込、file I/O、decode、大件数sort、watch scan |
| Application Core | command / queryの調停、revision、正本判断、route選択 | WPF control、Dispatcher、ObservableCollection、WebView2 DOM |
| ReadModel Pipeline | search / sort / filter背景計算、snapshot / diff生成 | UI control直接更新、永続化 |
| Scheduler | priority、bounded queue、coalesce、latest-only、release、drain | 機能固有のDBや画像処理 |
| Image Pipeline | visible-first、placeholder、存在確認、stamp、decode、stale破棄 | 一覧全体reload |
| Persistence Pipeline | 背景直列保存、状態遷移、retryability、shutdown drain | UI入力の同期待ち |
| Feature Runtime | Skin、Player、Watcher固有処理 | 共通優先制御やUI正本判断 |
| Worker境界 | thumbnail、rescue、metadata probeのUI非依存契約 | WPF、ViewModel、UI状態、`.wb`正本変更 |

### 3.2 データの流れ

通常経路は次に揃える。

`User Input -> UI Snapshot -> Core Command -> Scheduler -> Pipeline -> Revision付きResult -> Diff Apply -> Visual State`

watchやbackground処理は次に揃える。

`Background Event -> Change Set -> Scheduler -> ReadModel / Pipeline -> Revision Guard -> Diff Apply`

保存は次に揃える。

`User Input -> UI先行反映 -> Persistence Queue -> persisted / dirty / failed -> 必要最小限の通知`

### 3.3 全面再評価を許す条件

full reload / full recomputeは次に限定し、必ずreasonを残す。

- DB切り替え。
- 初期完全読込。
- query条件変更。
- sort key変更。
- 大量変更しきい値超過。
- `{dup}`、hashなど集合全体の意味が変わる変更。
- dirty fieldの影響を安全に判定できない変更。

それ以外はdiff-firstを通常経路とする。背景計算の最後に全面 `Refresh()` へ戻して体感改善を相殺しない。

## 4. 実行ロードマップ

進捗はパーセントで表さない。各Phaseを `未着手`、`契約済み`、`接続中`、`実機確認待ち`、`完了` のいずれかで扱う。

### Phase 0. 体感ベースラインと同一run証跡

状態: 実機確認待ち

目的: 「何となく重い」を操作単位の事実へ変える。

#### 2026-07-11 親レビュー

- `76ef865` で、既存contract / Phase 0 evidenceを主要8シナリオへ再分類するscorecardを追加した。log evidenceとselection / focus / scroll / blankなどの目視確認は分離し、目視未確認のままPhase 0完了にはしない。
- `74e2cb4` で、最新runの `ui hang updated:` に限ったdelay分布と、queue / stale / full fallbackのログ行要約を追加した。操作latencyや一意操作数とは呼ばない。
- サブエージェント検証はscorecard 27件、run metrics 24件のRelease x64 focused testが成功した。親レビューでも実装範囲、Author / Committer、`git diff --check` を確認した。
- 2026-07-10の最新runをlive auditへ通した結果は、contract evidence `5/9`、Phase 0 evidence `2/12`、scenario log evidence `1/8` だった。log evidenceが揃ったのは `persistence-shutdown` だけで、同シナリオも目視確認は未完了である。
- 同runの参考値は、UI停止delay sample 90件、p50 750ms、p95 1251ms、max 1498ms、最大queue深さ1、stale discard 0行、full fallback 2行だった。full fallbackは2行とも `reason=query` で、selection refreshとscroll resetを伴っていた。
- 最大queue深さ1のため、少なくともこのrunではqueue滞留を主因と断定しない。大きいdelayはWatch / Thumbnail / activityなしの各状態で観測したが、主要操作が同一runに揃っていないため、WatchやThumbnailを支配要因とも断定しない。
- `55bd5e0` で、コピー済み `.wb` の明示指定と確認フラグを必須にしたPhase 0診断起動スクリプトを追加した。DBの自動コピーや変更は行わず、起動した子プロセスだけへno-persist、コピーDB、Releaseログ設定を渡す。
- `4d32338` で、live auditの対象ログをliteral pathで解決し、監査用環境変数を実行後に復元して終了コードをそのまま返す監査スクリプトを追加した。終了コード1はスクリプト異常ではなく、現行ログの必須証跡不足を表す。
- サブエージェント検証は診断起動7件、live audit 5件のRelease x64 focused testが成功した。親レビューでも両方をまとめた12件が成功し、監査スクリプトの実行結果はscenario log evidence `1/8` のため想定どおり終了コード1だった。
- `8fb1ed5` で、診断ランチャーとscorecardへPhase 1の主選択、複数選択、scroll anchor、focus、250 ms操作表示、表示中の継続入力を目視項目として接続した。既存のscroll / PageUp / PageDown操作束は維持した。
- `af871c2` で、`phase0-manual-review-v1` の8シナリオ36項目をBOMなしUTF-8 + LFで生成するスクリプトと、schema、過不足、重複、statusを検証するlive audit連携を追加した。全項目が`pass`の時だけ目視確認を完了とし、log auditと目視確認のどちらかが未完なら非0を返す。
- サブエージェント検証はランチャー / scorecard 14件、目視記録 / live audit 8件がRelease x64で成功した。親レビューでは4テスト群22件とRelease x64全体buildが成功し、警告0、エラー0を確認した。
- 親の実行検証では、生成直後のJSONは`pending=36`、全項目passは`manual review: complete`、不正JSONは`invalid-json`になった。全項目passでも現行ログの必須evidenceが不足しているため終了コード1を維持し、目視だけでPhase 0を誤完了にしないことを確認した。
- `768b8f1` で、目視session開始ローカル時刻と最新runの先頭timestamp、sequence有無、開始sequence=1を照合する純粋policyを追加した。session境界env未指定時は従来のlog-only監査を維持し、指定時の境界未達は既存summary付きで失敗する。
- `642c692` で、目視JSONへ空sessionを追加し、診断起動直前にGUIDと開始ローカル時刻を一度だけ束縛するようにした。未使用36項目だけを起動へ通し、起動失敗時は元byte列へ復元する。live auditは構造不正・未束縛だけをdotnet前に拒否し、pending / fail / not_observedではlog auditを継続して最終結果だけ非0へ統合する。
- サブエージェント検証はsession boundary 17件成功 / opt-in 1件skip、script側21件成功だった。親レビューでは関連39件中38件成功 / opt-in 1件skipとRelease x64全体buildを確認し、警告0、エラー0だった。
- 親スモークでは、起動失敗時のJSONが元byte列と完全一致、成功時だけGUID / started_localが束縛、未束縛JSONはdotnet前拒否、pendingはlog audit継続、古いログは`run-before-session`、all-pass + 過去開始時刻は境界通過後に既存evidence不足で非0となることを確認した。manual未指定時はambient session envを子監査から外し、親環境へ元値を復元する。
- Phase 0は `実機確認待ち` のまま維持する。次は新しいログfieldを足さず、主要8シナリオと目視項目を同一Release runで採取する。

実行シナリオ:

1. cold startから `first-page shown`、`input ready`、`heavy services started` まで。
2. 検索入力中にsortを変更し、直後にscroll / PageUp / PageDownする。
3. 上側タブ、Logタブ、選択、ページを連続で切り替える。
4. 検索中にwatch 1件追加とrenameを発生させる。
5. Playerを開始、停止し、音量を変更する。
6. visible thumbnail、進捗、ERROR一覧、詳細画像を表示する。
7. コピーDB + no-persistでskin通常切り替えとHeader Reloadを行う。
8. 設定変更、bookmark / score / tag保存後に終了する。

安全な採取手順:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\New-Phase0ManualReview.ps1 -OutputPath "<目視記録.json>"
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\Start-Phase0DiagnosticRun.ps1 -CopiedDbPath "<コピー済み.wb>" -ManualReviewPath "<目視記録.json>" -AcknowledgeCopiedDb -Wait
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\Invoke-Phase0LiveAudit.ps1 -ManualReviewPath "<目視記録.json>" -NoBuild
```

1本目は未使用の目視記録JSONを作成する。2本目はユーザーが事前に用意したコピーDBと未使用JSONだけを受け付け、JSONを診断sessionへ束縛してRelease x64アプリを起動する。操作結果を同じJSONへ記録し、3本目で最新ログrunと目視sessionの境界を照合してまとめて監査する。監査の終了コードが0になるまでPhase 0は完了にしない。

成果物:

- 同一Release runの `debug-runtime.log`。
- 操作時刻と視覚結果を対応させた`phase0-manual-review-v1` JSON。
- 目視JSONのsession ID / started_localと、最新ログrunの先頭timestamp / sequence=1が一致する同一run境界。
- `phase0_audit_complete` と不足evidence一覧。
- p50 / p95、最大停止、stale discard、full fallback、queue depthの要約。
- 次に直す支配要因を最大3件に絞った判断。

scorecard、run metrics、目視記録テンプレートと監査は実装済みである。残る成果物は、主要8シナリオを通した同一runと、人間が判定したselection / focus / scroll / blankを含む36項目の実記録である。

完了ゲート:

- startup / search / sort / scroll / Player / watch / image / persistence / thumbnail / skinが同一runで追える。
- 必須contract evidenceとPhase 0 evidenceが揃う。
- 操作後の選択、フォーカス、スクロール、blank有無を目視記録できる。
- ログ不足を新しい実装不足と混同しない。

### Phase 1. UI Shellと操作連続性

状態: 実機確認待ち

目的: UI event handlerを、入力受付とvisual state更新へ限定する。

#### 2026-07-11 親レビュー

- 2系統の監査で、通常UIと外部skinのsortがインメモリ差分Move後にも `SelectFirstItem()` を実行し、保持できる選択とscrollを先頭へ戻していたことを最上位の静的阻害要因と判断した。
- `60d4b65` で通常UI、`0a38a20` で外部skinの通常sortから無条件先頭選択を除いた。起動partialの全件再取得時だけ先頭選択を残し、通常sortのstale / cancel時は後処理へ進まず、外部skin APIもfalseを返す。
- サブエージェント検証は通常UI 45件、外部skin 17件のRelease x64 focused testが成功した。親レビューでは両テスト群62件とRelease x64全体buildが成功し、警告0、エラー0を確認した。
- `4466d8a` で検索結果更新後、`0efd679` で上側タブ往復時の選択を条件付きfallbackへ変更した。どちらも現在選択を先に確認し、未選択時だけ `SelectFirstItem()` を実行するため、選択が生きている通常経路を先頭へ巻き戻さない。
- サブエージェント検証は検索7件、タブ1件が成功した。親レビューでは関連42件とRelease x64全体buildが成功し、テストの汎用source parserも39行の期待ブロック検証へ差し戻して簡素化した。
- `ca87d50` でViewModel内のstable key判定を `MovieViewStableKeyPolicy` へ分離し、既存diff / moveも同じID優先・path fallback契約を使うようにした。`efb4f30` でReset apply前の主選択キーを捕捉し、変更後の一覧に同じキーが残る場合だけ現行レコードを現在タブへ復元する。Diff / Move、対象消失、key解決不可では介入しない。
- サブエージェント検証はstable key側21件、Reset復元側54件が成功した。親レビューでは関連67件とRelease x64全体buildが成功し、警告0、エラー0を確認した。
- `ffdaee4` で主選択を先頭にした複数stable keyの捕捉と、Reset後の現行レコード一括解決を追加した。key重複は大小文字を区別せず除き、apply後一覧で同じkeyが複数ある場合は曖昧なため復元しない。`9deee65` でsnapshot順に既存のExtended選択反映へ接続し、対象が消えた場合は残存項目だけを戻す。
- サブエージェント検証は複数key policy 18件、UI接続45件が成功した。親レビューではstable key、複数選択、ReadModel apply、外部skin選択の関連89件とRelease x64全体buildが成功し、警告0、エラー0を確認した。
- `a998896` で先頭可視項目のstable keyとviewport相対top offsetを持つscroll anchor policyを追加した。`496b0f8` でReset前のanchor捕捉、Reset後の `ScrollIntoView`、container再実現、top差分によるoffset補正、visible range再計測を接続した。同一タブ・一意key・Reset変更ありの場合だけ同期layoutへ入り、取得失敗やteardown競合は現在位置を壊さずskipする。
- サブエージェント検証はscroll anchor policy 20件、WPF接続92件が成功した。親レビューではscroll、viewport、ReadModel、選択の関連113件とRelease x64全体buildが成功し、警告0、エラー0を確認した。
- `2b64fa8` で一覧内focus項目のstable keyを捕捉・一意解決するfocus anchor policyを追加した。`daabc6a` でReset前に現在標準タブ内へkeyboard focusがある場合だけanchorを捕捉し、選択・scroll・互換詳細更新後に、同一タブかつactive windowの実現済みcontainerへfocusを戻す。SearchBox、別ペイン、別タブ、非active window、未実現itemではfocusを奪わない。
- サブエージェント検証はfocus anchor policy 13件、WPF接続2件が成功した。親レビューではfocus、scroll、選択、ReadModelの関連124件とRelease x64全体buildが成功し、警告0、エラー0を確認した。
- `7588ddf` で250 ms遅延、検索 / sort / Player準備の文言、revision stale guardを持つfeedback policyを追加した。`aa81017` でuser-priorityの最初のBeginと最後のEndへ接続し、250 msを超えた時だけヘッダーのDB path領域へcompactなindeterminate表示を出す。ボタン、一覧、入力は無効化せず、終了時は即座にpath表示へ戻す。親レビューでnullable警告を修正し、最新コミットへamendした。
- サブエージェント検証はfeedback policy 14件、WPF接続49件が成功した。親レビューではfeedback、user-priority、検索、sort、Playerの関連128件が成功し、Release x64全体buildは警告0、エラー0だった。
- `8fb1ed5` と `af871c2` で、Phase 1の実機確認項目を主要シナリオと36項目の目視JSONへ接続した。静的Behaviorだけで完了にせず、同一runの操作継続性を記録できる状態になった。
- Phase 1は静的Behavior接続とRegression Guardが揃ったため `実機確認待ち` へ進める。VirtualizingWrapPanelのoffset単位、同期 `UpdateLayout()` の所要時間、Reset時のちらつき、複数SelectionChanged、実際のfocus位置、250 ms表示中も操作継続できることを同一Release runで確認するまで完了扱いにしない。

実装項目:

- event handlerは軽量snapshotとcommand発行を基本にする。
- 同期DB read / write、file I/O、decode、media probeをUI入口から除く。
- 検索、sort、DB切り替え、tab、Playerのbusy状態を局所化し、画面全体を不要に無効化しない。
- 選択、フォーカス、スクロール位置をstable keyで復元する。
- 250 msを超える明示操作は、進行中か継続利用可能かを画面上で分かるようにする。
- 入力中の背後処理抑止と解除を必ず対にし、timeoutは観測だけで勝手に状態を壊さない。

完了ゲート:

- search / sort / scroll / tab / Playerの各入口が `UiOperationSnapshot` から始まる。
- 連続操作後も最新操作だけが画面へ残る。
- 選択、フォーカス、スクロール位置の保持をfocused testと実機で確認する。
- UI thread上の同期I/O追加をsource policyで検出する。

### Phase 2. ReadModel StoreとDiff-first完成

状態: 接続中

目的: 一覧全体の作り直しを例外経路へ追いやる。

#### 2026-07-11 親監査

- 大件数sortのbackground実行、要求revision、後着cancel、計算後とUI apply直前のstale guardは実装済みである。今回の選択連続性修正ではこの境界を変えていない。
- 残る未達は、sort計算内部の協調cancel、先行sortと後着sortを競合させる実行test、watch 1件追加 / renameが単件change setのままUI applyされる実イベント証跡である。
- Grid系タブへDiffを一括拡大しない。VirtualizingWrapPanelの選択、scroll、ちらつきを実機で確認してから最小経路を選ぶ。

実装項目:

- `MovieRecords`互換表現とUI表示用ReadModelの境界を明確にする。
- stable key、source revision、view revision、query revision、sort revisionを一貫して運ぶ。
- add / remove / replace / moveを通常のUI applyにする。
- search / sortは背景計算し、後着入力でcancelまたはstale破棄する。
- watch 1件追加、rename、tag、score、thumbnail成功を局所反映へ通す。
- full fallbackの件数、理由、変更規模を集計し、安全なものだけ段階的にdiffへ戻す。

完了ゲート:

- watch 1件追加とrenameが `diff_change_set=single`、`diff_changed_total=1` のまま反映される。
- thumbnail成功、tag、score更新で一覧全体reloadへ戻らない。
- 大件数sort中に後着sortが来た場合、古い計算とapplyが残らない。
- `Items.Refresh()` は本体へ戻らず、直書き `Refresh();` と `FilterAndSort(..., true)` は許容線内に留まる。

### Phase 3. Scheduler一本化

状態: 接続中

目的: 機能ごとの個別延期を、共通の優先制御へ段階統合する。

優先順:

1. 入力、選択、scroll、tab。
2. Player明示操作。
3. 現在表示中の一覧diff。
4. visible画像。
5. 最新search / sort / filter。
6. watch小差分。
7. thumbnail / rescue / prewarm。
8. skin catalog / profile保存 / background repair。

実装項目:

- Everything poll、watch apply、thumbnail progressで得た最小runtimeを維持する。
- image refresh、skin refresh、ReadModel recomputeを、必要性の高い順に接続する。
- bounded capacity、coalesce、latest-only、priority preempt、fairnessを共通policyで決める。
- admission、take、release、timeout、shutdown pendingを同じsequenceで追う。
- user-priority解除漏れとqueue残留を検出する。

完了ゲート:

- search / sort / Player中にwatch、poll、thumbnailが操作完了を妨げない。
- 古い同一key要求がqueueへ増え続けない。
- background処理がstarvationせず、操作終了後に安全に再開する。
- shutdown時のpendingとdrain結果を説明できる。

### Phase 4. Visual ContinuityとImage Pipeline

状態: 接続中

目的: 一覧、詳細、Player、skinでblank、ちらつき、遅い画像差し替えを減らす。

実装項目:

- 上側タブ、下側ERROR / 進捗、詳細、Player右レールの要求を `ImageRequest` へ揃える。
- visible範囲を最優先にし、off-screen decodeは後ろへ送る。
- file存在確認、stamp、ERROR marker判定、decodeをUIスレッドから外す。
- placeholderを維持し、ready / missing / canceled / failedをrevision付きで反映する。
- stale image resultは対象keyとreasonを残して破棄する。
- skinはsame-document skip、catalog、persist、navigate、host clearを分け、不要なblankを作らない。
- Player surfaceは可能な限り再利用し、一覧やwatchの背後処理から守る。

完了ゲート:

- scroll中にoff-screen decodeがvisible画像を押しのけない。
- thumbnail成功後に対象画像だけが更新される。
- stale画像が後着表示されない。
- skin切り替え1回のblank、navigate、catalog、persist回数と時間を説明できる。
- Player開始 / 停止でsurfaceと選択表示が不必要に作り直されない。

### Phase 5. Persistence Pipeline完成

状態: 接続中

目的: 保存を待たずに操作でき、失敗時だけ適切に知らせる。

実装項目:

- settings、Player volume、playback stats、bookmark、score、tag、movie path、skin profileを共通方針へ揃える。
- UIは表示値を先に更新し、保存はkey単位の背景直列queueへ送る。
- `dirty`、`saving`、`persisted`、`failed`、`retryable` を区別する。
- 同一keyの高頻度保存はdebounce / coalesceする。
- DB切り替え後着を破棄し、別DBへ誤保存しない。
- shutdown drainのtimeout / failureを契約語彙で残す。

完了ゲート:

- 保存処理がクリック、入力、Player操作を同期で待たせない。
- 成功、retryable failure、通知対象failureをログだけで区別できる。
- 終了直前の設定と音量がbounded drainで回収される。
- no-persist診断でユーザーDBと設定を変更しない。

### Phase 6. Feature RuntimeとWorker境界

状態: 契約済み / 接続中

目的: Skin、Player、Watcher、thumbnail、rescue、metadata probeをUIから独立して失敗させられるようにする。

実装項目:

- Skin / Player / WatcherはCore routeからScheduler / ReadModel / Persistenceへ接続する。
- Watcherはchange set正規化に専念し、UI更新の実行者にしない。
- Player surface操作と再生統計保存を分離する。
- Worker request / progress / result / artifact DTOを既存in-process経路の正規語彙にする。
- WorkerはWPF、Dispatcher、ViewModel、UI状態を参照しない。
- failure、retryability、artifact、metricsを本体UI停止なしで扱う。

sidecar判断ゲート:

次のすべてを満たした場合だけIPC / sidecarを検討する。

- Phase 0からPhase 5が実機で閉じている。
- UI詰まりの支配要因がmedia computeまたは外部ツール境界に残っている。
- in-process cancellationとrevision guardだけでは隔離できない。
- process起動、IPC、artifact cleanup、version互換、shutdownの運用コストを上回る効果がある。

完了ゲート:

- Skin / Player / Watcherの処理が `core_route` から終端まで追える。
- worker failure / timeoutが本体UIとqueue全体を止めない。
- sidecarを導入しない判断も、実測に基づいて説明できる。

### Phase 7. 長時間運用とRelease卒業判定

状態: 未着手

目的: 短い成功例ではなく、日常利用でストレスが戻らないことを確認する。

検証項目:

- 主要8シナリオを大DBと通常DBで繰り返す。
- search / sort / scroll / tab / Player / watch / thumbnail / skinを30分以上混在させる。
- queue depth、memory、UI hang、stale discard、full fallback、保存失敗を観測する。
- DB切り替え、window close、shutdown、再起動を繰り返す。
- 選択、フォーカス、scroll、Player surface、skin表示の連続性を目視確認する。
- WhiteBrowser互換と`.wb`非変更を確認する。

完了ゲート:

- 体感予算をDB規模別に満たすか、超過理由と許容判断が記録されている。
- user-priority、busy、watch suppression、scheduler pendingが操作後に残らない。
- stale結果、blank、選択飛び、無反応、shutdown固着の再現がない。
- focused test、Release x64 build、source policy、実機ログ、目視確認が同じ変更系列で閉じる。

## 5. 実行順

### Wave A. いま行うこと

1. Phase 0の同一Release runを採取する。
2. scenario scorecardを作り、最大停止と操作連続性を記録する。
3. 支配要因を最大3件へ絞る。
4. 最上位1件だけを小さな実装計画へ分解する。

### Wave B. 一覧操作の主経路

1. Phase 1の選択 / focus / scroll保持を固定する。
2. Phase 2のwatch 1件追加 / renameと大件数sortを閉じる。
3. Phase 3へReadModel recomputeとvisible画像予約を必要最小限で接続する。

### Wave C. 表示と保存

1. Phase 4のImage / Skin / Playerのblankとstaleを閉じる。
2. Phase 5の保存状態とshutdown drainを閉じる。

### Wave D. 境界と卒業

1. Phase 6のCore routeとWorker failure隔離を閉じる。
2. sidecar要否を判断する。
3. Phase 7の長時間運用でRelease卒業を判定する。

後段Waveの抽象だけを先に作らない。常に、今の支配要因を減らす最小接続から進める。

## 6. テストと証跡

### 6.1 3つの完了条件

各変更は次の3条件が揃って初めて完了とする。

1. Behavior: ユーザー操作が実際に滑らかになり、表示互換を守る。
2. Evidence: `debug-runtime.log` とscenario scorecardで理由を説明できる。
3. Regression Guard: unit / focused / source policy / Release x64 buildで後退を検出できる。

### 6.2 テスト層

- Unit: revision、diff、priority、coalesce、stale、fallback、retryabilityの純粋判断。
- Focused: search / sort / watch / image / persistence / Player / skinの境界接続。
- Source policy: UI同期I/O、全面refresh、契約識別子、WorkerのWPF依存を監視する。
- Integration: DB切り替え、watch burst、Player、skin、shutdownを実経路で確認する。
- Release x64: Debugだけで成立する挙動やログを完了扱いにしない。
- Manual scenario: 選択、focus、scroll、blank、ちらつき、無反応は人間の目で確認する。

### 6.3 実機ログの最低項目

- `request_id` またはrouteを追える識別子。
- source / view / query / sort / image revision。
- trigger、reason、skip reason、fallback reason。
- elapsed ms、item count、changed count、queue depth、priority。
- cancellation / stale discard / release reason。
- visible state、selection impact、scroll impactを判断できる補助情報。

ログを増やすこと自体を成果にしない。既存項目で判断できる場合は、新しい語彙を追加しない。

## 7. 禁止線

- `.wb`スキーマを変更しない。
- WhiteBrowser互換を壊さない。
- 本体一覧を即時WebView2化しない。
- `MainWindow`の責務を巨大なCoreやViewModelへ丸移ししない。
- UI event handlerへ同期DB、file I/O、decode、media probeを戻さない。
- 背景化した最後に全面 `Refresh()` して効果を消さない。
- user-priority中にbackground full reloadを押し込まない。
- 難読動画対応を通常動画hot pathへ混ぜない。
- 観測性を削って速く見せない。
- Base64画像搬送を導入しない。
- UI行ごとの `File.Exists(...)` を復活させない。
- 補助index / cacheを`.wb`の代替正本にしない。
- 実測なしでIPC / sidecarを導入しない。
- ログ契約の補強だけでPhase進捗を上げない。

## 8. リスク管理

| リスク | 対応 |
|---|---|
| diff適用で表示互換を壊す | full fallbackを残し、selection / focus / scrollをテストする |
| 背景化で後着更新が増える | cancellationとrevision guardをapply直前まで通す |
| Scheduler統合でstarvationする | bounded queue、fairness、release log、操作後catch-upを持つ |
| 楽観UI更新とDBが不一致になる | dirty / saving / persisted / failed / retryableを分ける |
| ログがhot pathを重くする | 通常成功の全件ログ化を避け、aggregateとsamplingを使う |
| skin診断でユーザー環境を汚す | コピー`.wb`とno-persist診断だけを使う |
| Release出力が実行中でlockされる | プロセスを止めず、一時出力でbuild検証する |
| 小口改善が目的化する | Phase 0の支配要因3件以外は着手順を上げない |

## 9. ドキュメント運用

- 本文は長期判断、体感目標、Phase Gate、実行順だけを持つ。
- 日々の小口レビュー、focused test件数、個別ログfield追加はGit履歴と対応コミットへ残す。
- 大粒度優先順位は `AI向け_現在の全体プラン_workthree_2026-03-20.md` を優先する。
- 日々のUI高速化着手順は `Docs\forAI\Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md` を優先する。
- 上位ゴールと禁止線は `Docs\forAI\Goal_Indigoの未来図_2026-05-28.md` と整合させる。
- この正本を更新した時は、更新日、判断変更、次の最優先、残る実機確認を明記する。
- 進捗率は使わず、Phase状態と未達ゲートで現在位置を示す。

## 10. 次の最優先

次の一手は、新しい抽象やログ契約の追加ではない。

同一Release runで主要8シナリオを通し、次の3点を確定する。

1. ユーザーが実際に待たされる最長区間はどこか。
2. 選択、focus、scroll、blankのどれが体感を悪化させているか。
3. full fallback、queue競合、同期I/O、decode、navigateのどれが支配要因か。

監査summaryには `phase0_scenario_log_evidence`、`phase0_scenario_scorecard`、`phase0_manual_visual_review`、`phase0_run_metrics` が出る。次回採取では、この4行と実表示の記録を同じrunへ揃える。

次回採取は `New-Phase0ManualReview.ps1` で36項目の記録を作成し、同じpathを `Start-Phase0DiagnosticRun.ps1 -ManualReviewPath` へ渡してsessionを束縛する。操作後に同じpathを `Invoke-Phase0LiveAudit.ps1 -ManualReviewPath` へ渡し、log evidence、目視記録、同一run境界をまとめて閉じる。

シナリオ2ではGrid系のResetを含む検索と通常sortの後に主選択・複数選択・先頭可視項目のtop位置・一覧内keyboard focusが飛ばないこと、250 ms未満では表示せず、超えた時はヘッダー表示中も入力とscrollを続けられることを確認する。シナリオ3では上側タブ往復後に各タブの既存選択が残り、SearchBoxや別ペインのfocusを奪わないことを目視し、今回の修正をBehavior証跡で閉じる。Wrap系とList系のoffset差、同期layout時間は別々に記録する。外部skin sortはコピーDB + no-persistで同じ保持を確認するまで実機完了扱いにしない。

2026-07-12の最上位所見には最小変更を入れ、2回目検索のquery-only化、入力中のサムネイル表示延期と新規lease延期、live監査のrun分離とrotation跨ぎ復元、Player右レールのscroll中decode分離をRelease実機で確認した。旧3列 `VirtualizingWrapPanel` では物理ホイール中に `activity=None` のUI停止が最大1249 ms残ったため、Player右レールだけを標準 `VirtualizingStackPanel` の固定高56 px縦リストへ変更した。選択、クリック、context menu、Recycling、item scroll、0.5 Page cacheは維持し、他タブのwrap表示は変更していない。

同日Release x64のコピーDB + no-persist診断では、Player PageDownの一覧処理が3 ms / 7 ms、入力開始から最初のRenderが6 msだった。標準縦リスト化によってPlayerスクロールの初動は短くなったと判断する。一方、同じバースト中に `activity=None` のUI監視停止が最大1103 ms残っており、Playerレイアウトだけで全停止が解消したとは扱わない。次はユーザー自身の物理ホイールで操作感を判定し、引っ掛かりが残る場合は同時刻の背景処理とUI停止を別件として切り分ける。ここから先の進捗は、契約数ではなく、ユーザーの待ちと表示の乱れが減ったかで判定する。

同日の次フェーズでは、rescued thumbnail 反映が1件ごとに `DispatcherPriority.Normal` へ入り、`MainVM.MovieRecs` を毎回全走査していた経路を最上位1件として修正した。最大16件のbatchをDispatcher 1回へ畳み、UI-bound collectionの索引もbatchごとに1回だけ作る。user-priority中は120 ms単位で延期し、DB session / path / shutdownをapply直前にも確認する。Release x64の新しいコピーDB + no-persist runでは、rescued sync 4回の `apply_ms=0〜3`、`dispatch_wait_ms=0〜5` で、同時刻に新しいUI hangは記録されなかった。次順位は、Player scroll中の `UpperTabPreferredMoviePathKeysRevision` 更新と画像Binding再評価をRender単位または短いlatest-onlyへ合流すること。ただし、ユーザー自身の物理ホイール確認前にPlayer scrollフェーズを完了扱いにはしない。

続くrevision監査では、Player warm / viewport更新が共有revisionを通じて通常5タブのMultiBindingも再評価していたため、`PlayerRightRailImageRevision` を専用化した。Player由来のviewportとwarmは専用revisionだけ、通常タブviewportは共有revisionだけ、サムネ実体変更は両方を進める。Release x64実機ではPlayer操作中とwarm反映の双方で `shared_revision_updated=False player_revision_updated=True` を確認した。一方、同じrunのUI停止は最大1249 ms残り、最初のRenderは22 msだった。したがって他タブへのrevision波及は削減できたが主因ではない。次の最上位候補はPlayer内で実現された行のBinding / converter呼出数とWPF layoutであり、物理ホイールの目視結果と同時刻の実測を揃えてから1件へ絞る。

Player内の1バースト集約計測を追加した結果、8回PageDownで `revision_delta=7 converter_count=227 generator_delta=194 max_layout_gap_ms=944` を観測した。viewport revisionをscroll priority中はpendingへ畳み、idle時のvisible warmと最大1回に合流すると、同条件で `revision_delta=0`、idle反映1回、`converter_count=104` まで減った。ただし `generator_delta=198 max_layout_gap_ms=903`、UI停止最大1247 msで、Binding再評価削減だけでは停止は閉じなかった。

仮想化先読みを `0.5 Page` から `0` へ下げる比較も行ったが、`generator_delta=228 max_layout_gap_ms=883`、UI停止最大1203 msとなり、container再生成が増えて改善しなかったため同日中に `0.5 Page` へ戻した。次順位は固定高56 px行のtemplate軽量化で、`Label` / itemごとのContextMenu / ToolTipがcontainer生成へ与えるコストを小さく比較する。cache `0` は再採用しない。ユーザー自身の物理ホイール確認まではPlayer scrollフェーズを完了扱いにしない。

固定高行は画像ラッパーを `Label` から `Border` へ変え、タイトルの行ごとのToolTip Bindingを外した。クリック処理は `FrameworkElement.DataContext` 基準へ寄せ、既存Label senderとPlayer Border senderを同じ経路で扱う。8回PageDown比較ではgenerator数は198で不変だが、最大layout gapは903 msから846 msへ低下し、スクロール中のWarning停止は出なかった。

さらにカーソル位置のnative window handleが本体handleと一致することを確認してOSホイール入力を送り、12送信中8入力の1バーストで `first_render_ms=6 first_layout_ms=5 converter_count=8 generator_delta=36 max_layout_gap_ms=637 revision_delta=0` を採取した。旧物理ホイールrunの最大1249 ms停止より改善し、同バースト中にWarning停止は記録されなかった。自動入力上のBehavior / Evidence / Regression Guardは揃ったが、人間の物理ホイール操作感は未確認なのでPlayer scrollフェーズは実機確認待ちのままとする。

次のユーザー報告である検索Textbox無反応を監査し、Editable ComboBoxのBindingが `DbInfo.SearchKeyword` を先に更新した後、debounce側が同値を理由に検索を捨てる経路を修正した。TextChanged hot pathはIDと時刻のメモリ更新だけにし、ログはdebounce時1回へ集約する。部分ロード中の `startup-feed-partial` skipも廃止し、500 ms確定後は検索正本へ進める。Release x64コピーDB実機ではEnterなしの `movie` 入力が `search_input_id=1` でdebounce発火後14 msにfilter開始し、357件へ絞り込まれた。

初回検索全体は7868 msで、`db_reload_ms=1144 source_apply_ms=6643 filter_sort_ms=50 apply_ms=23` だった。入力が黙って捨てられるBehaviorは閉じたが、部分ロードからの初回full reloadは依然遅い。次の最上位はDB readではなく全件source変換の6.6秒であり、source snapshot / MovieRecords生成を検索要求ごとに繰り返さない境界を調査する。

source変換監査では、管理サムネ欠損時の同名source image探索がSmall / Big / Grid / List / Big10 / Detailの各用途から同じ行で繰り返され、43k件では最大77万回級のファイル確認になり得ることを確認した。グローバルcacheは作らず、1行内だけのlazy resolverで探索結果を共有し、管理サムネが見つかる行ではprobeを起動しない。

Release x64コピーDBの同じEnterなし検索では43,433行に対し `source_image_probe_count=43433 source_image_cache_hit_count=173792`、初回 `source_apply_ms=3300 total_ms=4987`、後続 `source_apply_ms=2345 total_ms=4080` だった。修正前の `source_apply_ms=6643 total_ms=7868` から初回source変換を約50%短縮した。次順位は残る2.3〜3.3秒のDataRow型変換、タグsplit / Regex、MovieRecords allocation、collection replaceを区間分解し、効果の大きい1件だけを選ぶ。

次の分解計測では、bulk cache / row convert / source image probe / MovieRecs replace / exists予約 / ERROR invalidateを1行へ集約した。同時にDB日時3列が既に正規 `yyyy-MM-dd HH:mm:ss` 文字列なら、妥当性確認後に同じ参照を返し、43k件で約13万回のparse / format / allocationを避けるfast pathを追加した。

Release x64の43,488行実機では `source_apply_ms=2017`、内訳は `bulk_cache_ms=2 row_convert_ms=1868 source_image_probe_ms=1223 replace_movie_recs_ms=6 queue_movie_exists_ms=0 invalidate_thumbnail_error_ms=0` だった。UI差し替えは支配要因ではなく、row convertの約65%を実ファイルprobeが占める。検索全体は4349 msまで短縮した。次順位は43k件すべてをfilter前にprobeする順序を見直し、visible / filtered結果を先に返して同名source image解決を後段へ送れるかを設計する。

同日、full reloadのbulk変換では同名source image探索を行わず、filtered結果とnear-visible範囲が確定した後だけ背景probeする経路へ移した。DB path、filter revision、probe revision、現在レコード参照で後着結果をguardし、user-priority中は開始を延期する。探索対象はplaceholderが残るnear-visible行だけで、完了時もplaceholder用途だけを書き換えるため、その間に生成された管理サムネイルを上書きしない。

Release x64コピーDBのEnterなし `movie` 検索では43,548件級の全件probeが43,548回から0回となり、`row_convert_ms` は従来約2秒から575〜745msへ短縮した。`source_apply_ms` は実行競合により1081〜2596ms、検索全体は2729〜4070msだった。near-visible probeは40〜104件に限定され、今回のDBではsource画像解決0件だった。Player表示中にWatch full fallbackとサムネ生成が重なると `activity=None` / `Thumbnail` のUI遅延警告が残ったため、次の最優先はscroll user-priority中のWatch UI apply・サムネ進捗/生成後反映の延期契約を実測し、残る同期処理を一つずつ外すことである。

続くフェーズでは、Playerのwheel / Page操作開始前から走っていたWatch scanが操作中に完了する競合を塞いだ。遅延reloadは要求をconsumeする前にuser-priorityを確認し、同じrevisionとキャンセルtokenで次の遅延窓へ再予約する。新しいWatch要求が来た場合は従来どおり旧tokenがcancelされるため、解除後も最新1件だけがapplyへ進む。

サムネイル側は、進捗fallback timerを操作中は既存snapshot coalesceへ戻し、生成後のMovieRecords反映を `DispatcherPriority.Background` へ下げた。受付時とDispatcher適用直前の双方でuser-priorityを確認し、成功結果は破棄せず120 ms単位で延期する。生成成功後の局所refreshも単一timerへ戻してlatest-onlyを維持する。親レビューではWatch、user-priority、Player、サムネ進捗・成功反映の関連162テストとRelease x64 buildが成功し、警告0、エラー0だった。

新しいRelease x64コピーDB runのPlayer PageDown 8回は `first_render_ms=27 first_layout_ms=25 max_layout_gap_ms=203 total_ms=507 revision_delta=0` だった。旧同条件の `max_layout_gap_ms=846` から短縮したが、今回のrunではWatch UI applyやサムネ成功反映そのものがスクロール区間へ重ならず、延期ログの実機観測は未達である。また同区間に `activity=None delay_ms=1163` が1件残り、viewport更新ごとのvisible source image probeがrevision 3〜9でstale完了を連続記録した。次の最優先はscroll user-priority中のvisible source probe予約を起動せず最新1件へ畳み、解除後1回だけ探索して、残るUI監視停止との因果を再測定することである。人間の物理ホイール体感確認も引き続き完了ゲートに残す。

次フェーズでは、visible source image probeをuser-priority中に毎回起動せず、最新reasonだけを保持する単一deferred workerへ集約した。解除後は `DispatcherPriority.Background` から通常Queueへ1回戻り、その時点のDB / filter revision / near-visible targetsを新しくsnapshot化する。親レビューでは延期受付時にprobe revisionを進め、操作開始前から待っていた旧viewport探索も解除直後に実行されないよう補強した。管理サムネイル保護、placeholder限定反映、全件走査禁止は維持している。

Release x64の関連111テストは成功した。コピーDBのPlayer PageDown 8回では `first_render_ms=28 first_layout_ms=26 max_layout_gap_ms=355 total_ms=658 revision_delta=0` となり、旧runでスクロール中に7件出ていたvisible probeのstale完了は0件になった。解除後は最新63件のprobeが完了したが、warm / viewport更新由来とみられる同一63件probeが約1秒後にもう1回完了した。また同区間のUI監視には `activity=None delay_ms=1238` が残る一方、描画側最大gapは355 msだった。次の最優先は同一DB / filter revision / preferred key snapshotの短時間重複probeを省き、UI hang監視値が実描画停止か監視上の遅延かを同じburst IDで照合することである。人間の物理ホイール体感確認は未達のまま残す。

次フェーズでは、visible source image probeのDB path、filter revision、順序付きplaceholder対象keyからfingerprintを作り、正常完了後2秒以内の同一要求を省いた。stale / failedは完了cacheへ入れず、placeholder対象が変われば同じviewportでも再探索する。Player scroll burstには単調な `burst_id` を発行し、priority begin / end / burst集約とUI hang detected / updatedを同じIDで結んだ。親レビューでは、一度scrollした後もactive扱いが残る誤りを修正し、実priorityフラグをvolatile snapshotへ渡した。

Release x64コピーDBでは `burst_id=1` のUI hangが `delay_ms=1007`、同burstの `first_render_ms=28 max_layout_gap_ms=332 total_ms=559` となり、Background heartbeatの待ちと描画gapに差があることを確認した。heartbeat予約を `DispatcherPriority.Input` へ変更して入力応答基準に揃えた次runでは、`delay_ms=1129 max_layout_gap_ms=999 total_ms=1556` と一致して実停止を観測した。同runの未warm領域では `cache_miss_count=104 queue_enqueued_count=104` だったため、scroll中のwarm enqueueを次の比較対象に選んだ。

warm queueへ軽量suspension providerを接続し、scroll priority中のmissは `Suppressed` としてキューへ入れず、idle時の既存viewport revision flushで再評価するようにした。通常enqueue / duplicate / capacity 64 / single workerとUI thread decode禁止は維持した。親統合74テストとRelease x64 buildは成功し、警告0、エラー0だった。実機では `cache_miss_count=40 queue_enqueued_count=0 suppressed_count=40` と抑止自体は効いたが、同じPageDown 8回は `first_render_ms=22 max_layout_gap_ms=2053 total_ms=3583` となり改善しなかった。したがってwarm decode投入はscroll中から除去できたものの主因ではない。次の最優先は、user-priority開始前から実行中のthumbnail worker / 外部decodeによるCPU競合と、WPF container生成約200件のどちらがlayout gapを作るかを同一burstで分離することである。自動PageDownは物理wheelの代替完了条件にせず、人間の物理ホイール体感確認を残す。

分離計測として、`ThumbnailProgressRuntime` にUI一覧を作らない `ThumbnailWorkerLoadSnapshot` を追加し、burst開始 / 終了のactive worker数と並列数を既存集約ログへ載せた。`ItemContainerGenerator.StatusChanged` では `GeneratingContainers` から `ContainersGenerated` までの周期数と最大時間だけをStopwatchで測る。全item走査、process列挙、追加ログ行は増やしていない。親統合79テストとRelease x64 buildは成功し、警告0、エラー0だった。

コピーDBのSendKeys PageDown 8回では `max_layout_gap_ms=1074 total_ms=1624 worker_active_begin=0 worker_active_end=0 generator_cycle_count=96 max_generator_cycle_ms=5` となり、thumbnail workerとcontainer 1周期のどちらも1秒gapを説明しなかった。そこで同じrunへ `SetForegroundWindow + keybd_event(VK_NEXT)` のWin32ネイティブ入力を8回送ると、`first_render_ms=8 first_layout_ms=8 max_layout_gap_ms=294 total_ms=519 worker_active_begin=0 worker_active_end=0 generator_cycle_count=98 max_generator_cycle_ms=5 cache_miss_count=104 queue_enqueued_count=0 suppressed_count=104` となり、burst中のUI Warningも出なかった。従来の1〜2秒gapはUIA経由の `SendKeys.SendWait` が入力配信間で止まった計測汚染と判断し、性能根拠から除外する。今後の自動scroll回帰はWin32 native inputとburst IDを使い、UIAはfocus / tab選択だけに限定する。Player自動回帰は良好だが、人間の物理ホイール操作感は未確認なので完了ゲートに残す。

visible source image probeは、正常完了後2秒cacheに加えて同一fingerprintのin-flight要求へ合流するようにした。同じDB / filter revision / ordered placeholder target群ならrevisionを進めず新Taskを作らず、異なるfingerprintだけが旧要求をstale化する。成功 / stale / failureの全経路で、finallyは自分が所有するfingerprintだけをCASで解除するため、後着要求を消さず失敗後も再試行できる。親統合64テストとRelease x64 buildは成功し、実機native burst解除後の63件probeはstale + successの2本からsuccess 1本へ減った。

UI hang相関は、heartbeat sampleへDispatcher投入時のStopwatch timestampを保持し、Player burst snapshotの開始timestamp以後に投入されたsampleだけを同じ `burst_id` と `scroll_active=true` へ結ぶよう補正した。burst開始前から滞留していたheartbeatは、burst中にログ化されても `burst_id=0` とする。親統合68テストとRelease x64 buildは成功した。最終Release x64コピーDBのnative PageDown 8回は `first_render_ms=69 first_layout_ms=66 max_layout_gap_ms=74 total_ms=831 worker_active_begin=0 worker_active_end=0 generator_cycle_count=101 max_generator_cycle_ms=33 cache_miss_count=40 queue_enqueued_count=0 suppressed_count=40`、burst中のUI hangログなし、解除後visible probeは63件の成功1本だった。自動回帰上のPlayer scrollは良好と判断する。完了ゲートには人間の物理ホイール操作感だけを残し、本線の次順位は初回filter full reloadの待ち時間へ戻す。

初回filterは、startup partial feed中だけ現在の `MainVM.MovieRecs` を既存 `SearchService` / sortで先に計算して表示し、その後 `FilterAndSortAsync(..., true)` をfire-and-forgetで開始して全件sourceへ整合する二段階経路へ変更した。検索専用revisionと既存global cancellationを併用し、新しい検索が来た時は古いpartial / full結果を反映しない。通常時query-only、検索履歴、選択保持、DB非変更、例外ログ契約は維持している。親統合80テストとRelease x64 buildは成功し、警告0、エラー0だった。

Release x64コピーDBでstartup partial 200件の時点にEnterなし `movie` を入力すると、partial-firstは5件を `snapshot_ms=1 filter_sort_ms=1431 apply_ms=26 total_ms=1434` で表示し、後続full reloadは43,573件から357件を `db_reload_ms=812 source_apply_ms=1478 filter_sort_ms=105 apply_ms=697 total_ms=3099` で整合した。従来は最初の表示もfull完了まで待っていたため、最初の有用結果を約1.67秒前倒しした。一方、200件のpartial計算自体が1431msで目標300msに未達である。`RefreshMovieViewFromCurrentSourceAsync` は元から常に `Task.Run` で計算しており、追加のforce-background変更は通常小件数をUI threadへ戻す回帰になるため親レビューで撤回した。次の最優先はpartial source向けASCII検索投影のcold初期化を入力前のidleへ移せるかを測り、検索仕様を変えず最初の5件表示を300msへ近づけることである。

続くフェーズでは、first-page shown / input ready後に現在のpartial source先頭最大200件だけをsnapshot化し、ASCII fast検索投影を単一background taskで事前生成するようにした。日本語phonetic fallback、全件走査、DB再読込は行わない。workerはlatest-onlyで、16件ごとにuser-priority、startup session、DB path、shutdownを確認し、Player scrollや入力が始まれば次の小区切りで中断する。親レビューでは境界4テストとRelease x64 buildが成功し、コピーDB実機は `completed=200 requested=200 elapsed_ms=10` だった。入力可能ログより後に開始し10msで完了したため、このrunではPlayer scroll時間帯へ背景処理を残していない。次は同じstartup partial条件で初回検索を再測定し、1431msのcold costが実際に解消したかを数値で判定する。

同条件の再測定では、prewarmは200件を12msで完了し、Enterなし `movie` のpartial-firstは5件を `snapshot_ms=1 filter_sort_ms=101 apply_ms=47 total_ms=104` で表示した。導入前の `filter_sort_ms=1431 total_ms=1434` から大幅に短縮し、最初の有用結果を300ms以内に返す目標を達成した。コード監査でもprewarmとpartial-firstが同じ `MovieRecords` 参照と `asciiFastSearchFieldCache` を再利用し、200件では日本語fallbackなしのfast projectionを通ることを確認した。更新時cache invalidate、input ready後の予約順序、200件境界を含む親関連78テストもRelease x64で成功した。このフェーズはBehavior / Evidence / Regression Guardが揃ったため完了とし、次順位はpartial表示直後に続く全件整合 `total_ms=2806` がPlayer scroll開始後にもCPU / UI applyで割り込まないかを同一runで確認する。

起動first pageの再計測では、DB読込前に5タブ＋詳細のサムネイルディレクトリを全件列挙する完全cache構築が残り、`startup open begin`から`startup page load`まで7.6〜9.3秒を占有していた。startup partialだけは空cacheから始め、各ページで必要な現行名・旧名候補だけを`File.Exists`で確認して同じcacheへ増分追記するようにした。continuationはcacheを再利用し、通常の全件reloadは従来の完全cache構築を維持する。各背景段階後にはstartup session、DB path、shutdownのlatest-only guardを置いた。最終Release x64コピーDBは`db_read_ms=50 bulk_cache_ms=41 row_convert_ms=36 total_ms=131`、`first-page shown=481ms input ready=482ms`となり、旧8117〜10187msから約94〜95%短縮した。表示互換、error marker、tags、source image、DB非変更を保ち、focused testとRelease x64 buildも成功したため、この起動first pageフェーズを完了とする。

続くフェーズでは、partial結果を維持したまま全件整合要求をlatest-onlyへ畳み、`ApplicationIdle`で開始するようにした。user-priority開始時は実行中整合の外部tokenをcancelし、DB読込後の43k変換は64件ごと、`ReplaceMovieRecs`直前と最終apply直前はtoken確認に加えて`DispatcherPriority.Background`で未処理Inputへ先を譲る。通常full reloadのDispatcher優先度は変えず、DB / search revision / shutdown guardと解除後の最新1件再開を維持した。親レビューでは逆順lockの可能性と通常経路までBackgroundへ下げる誤りを修正し、Release x64関連60テストが成功した。

コピーDBの検索後Player PageDown実測では、一度目のburst中にfull整合の開始は0件で、`first_render_ms=32 max_layout_gap_ms=104 total_ms=744` だった。解除後にrevision 2のfull reloadが始まった63ms後、二度目のPageDownでuser-priorityを開始すると `filter canceled: revision=2 stage=db-reload elapsed_ms=1509` となり、source変換とUI差し替えは行われなかった。解除後はrevision 3だけが再開・完了したため、開始抑止、後着キャンセル、latest-only再開は実機で確認できた。一方、DB facadeの同期読込はtokenで即時中断できず完了後にcancel判定するため、二度目burstは `first_render_ms=19 max_layout_gap_ms=893 total_ms=1615` だった。このフェーズはUI破壊防止まで完了とし、スクロール体感の次順位はDB読込へ協調キャンセルを通せるか、または連続スクロールが完全に静まるまでfull開始を延ばすquiet windowの比較である。

DB読込へ`SQLiteCommand.Cancel()`を登録する比較も行ったが、実DBの`DataAdapter.Fill`では取消後もDataTable materializeが残り、旧 `stage=db-reload elapsed_ms=1509` に対して新runは2423ms、user-priority開始後も約1506ms走った。さらに解除とactive処理のfinallyが再queueを競合し、revision 3/4、5/6の二重full reloadを観測した。効果のないDB取消とそのsource契約テストは正式revertし、DataTableを手作業で再構築する案も型・性能契約のリスクが大きいため不採用とした。二重起動はactive CTSが存在する間のqueueを拒否し、active処理のfinallyだけがCTS解放後に最新pendingを1回再queueするsingle-flightで修正した。

代替として、pending全件整合がある状態でuser-priorityが始まった時だけquiet windowをarmし、最後の解除から1500ms経過するまで単一delay taskで開始を待つようにした。操作がない初回fullは従来どおりApplicationIdleへ進み、連続操作は期限だけを延長する。既にApplicationIdle要求が積まれた競合窓でも、開始入口でquiet状態を再確認して同じdelayへ戻す。Release x64契約15件は成功した。単一診断プロセスの再採取ではpartial-firstは `filter_sort_ms=101 total_ms=107`、Player priority中のfull開始は0件だった。ただしPlayerタブ切替で始まった `reason=player` が採取終了まで解除されず、quiet解除後にfullが1回だけ再開する実機証跡は未取得である。同じPlayer burstは `first_render_ms=10 max_layout_gap_ms=1270 total_ms=2052` であり、このrunでは全件整合が動いていないため、次の最優先はPlayer開始処理がuser-priorityを長期保持する理由と、スクロール停止との同時刻相関を切り分けることである。

Player開始priorityは、MediaElement / WebViewの完了イベントだけに依存せず、要求revision付き250ms timerで有限解放するようにした。MediaOpened / MediaFailed、WebView成功 / 失敗、source設定例外、連続選択のsuperseded、reset、shutdownを単一解放口へ寄せ、timeout時も再生要求や再生面は止めずuser-priorityだけを返す。Player関連41テストとRelease x64 buildは成功した。

親実機レビューでは、quiet windowの既存ApplicationIdle要求がbegin基準期限後に遅れて到達すると、release基準の延長を飛ばす競合を発見した。開始入口を `armed && (releasePending || remaining > 0)` とし、releasePendingなら必ず再queueへ戻して最終releaseから1500ms延長する契約へ修正し、Release x64契約16件が成功した。最終単一プロセスrunはPlayer revision 1を `release_reason=superseded`、revision 2を `release_reason=timeout` で解放し、user-priority全体も379msで終了した。PageDown 8回は `first_render_ms=12 first_layout_ms=8 max_layout_gap_ms=224 total_ms=353`、full整合は最終releaseから3409ms後にrevision 2の1件だけが開始・完了し、scroll中のfull開始と二重起動は0件だった。自動native入力上はPlayer開始priorityと検索full整合の競合を閉じた。人間の物理ホイール体感確認は引き続き最終ゲートに残す。

続くWatch監査では、bulk scanの`source-inserted`を局所反映へ変える案を2回比較した。最初のpolicy案はbulk時に新規行が`MainVM.MovieRecs`へ載らず実機効果0、次のbatch append案はbulk条件の誤り、SQL文字列組立、full fallback未接続があり、責務跨ぎに対して安全性を閉じられないため両方ともコミットせず撤回した。`source-inserted` full fallbackは正しさ優先で維持する。

低リスク側として、Watch folder scan中の`skip_zero_byte` / `skip_failure_state`をscan-local集約へ移した。reason別に従来形式のsampleを先頭3件だけ残し、4件目以降はログ文字列自体を生成しない。scan endへ`skip_counts=reason:count`を追加し、failure-state詳細を含む内部Outcome、ERROR marker作成、例外・必要結果ログは維持した。親Release x64 92テストは成功した。コピーDB再runは既存ファイルだけだったため `scan_bg_ms=6178 skip_counts=none`、同区間のUI hangはCaution 500msが1件でWarningなしだった。旧runでは1秒級Watch Warningが反復していたが条件が異なるため、改善断定はせず、非zero `skip_counts`とsample最大3の実機証跡を次回新規/zero-byte検出時に採る。

同じscan直後にmissing-thumbnail rescueがfailure-state対象を再び1件ずつ出力していたため、rescue execution scopeごとの集約も追加した。block reason別に先頭3件だけ従来sampleを残し、4件目以降は文字列生成せず、finallyの中立な`rescue summary`へ`failure_state_skip_counts=reason:count`を出す。stale / user-priorityによる早期returnをfinishedと誤記せず、enqueue結果、ERROR marker、例外、block reason詳細は維持した。親Release x64 90テストは成功した。コピーDB再runではrescue自体が発火しなかったため、非zero summaryとsample最大3の実機証跡は次回rescue発火時へ残す。

同一probeはDB path、filter revision、順序付きplaceholder対象keyのfingerprintで識別し、正常完了または解決0件だけを2秒間省略する。stale / failedはcacheせず、重複skipではprobe revisionを進めないため、進行中の正しい探索を後着要求がstale化しない。UI hang heartbeatは単一pendingと250 ms間隔を維持したまま `DispatcherPriority.Input`へ上げ、Playerのburst IDとscroll activeをhang detected / updatedへ接続した。

次に、Player右レールのconverter missがscroll中にもwarm queueへ入り、画像decodeを背景で開始していた経路を閉じた。WarmQueue内部へ例外安全なsuspension providerを1本だけ接続し、user-priority中はpending keyやcapacityを変更する前に `Suppressed` を返す。idle時は既存のviewport revision pending flushでBindingを再評価するため、現在のrealized / visible要求だけが通常queueへ戻る。staticな判断状態をWarmQueue外へ増やさず、判定例外時は抑止しない。

Release x64の関連71テストと本体buildは成功し、警告0、エラー0だった。コピーDBのPageDown 8回では `converter_count=104 cache_hit_count=64 cache_miss_count=40 queue_enqueued_count=0 queue_duplicate_count=0 suppressed_count=40 revision_delta=0 viewport_revision_pending=True` を確認した。解除直後はpending revisionを1回更新し、その後 `visible_completions=18` の現在可視要求だけが通常warmへ戻った。未warm 40件が操作中にdecode queueへ入る問題はBehavior / Evidence / Regression Guardまで閉じた。

同runの `first_render_ms=22 first_layout_ms=21` は良好だが、`max_layout_gap_ms=2053 total_ms=3583` とUI監視の遅延は残った。このrunでは事前のUI Automation全探索が対象UIへ負荷を与えたため、停止値を製品単独の回帰とは判定しない。次の最優先はUIAを使わない実OSホイールrunで同じburst相関を採り、container生成、heartbeat、Watch / thumbnail activityのどれが残る長いgapと一致するかを1件へ絞ることである。人間の物理ホイール体感確認は完了ゲートに残す。

## 11. 前提

- WPF一覧を本線として維持する。
- `.wb`はWhiteBrowser互換の正本として変更しない。
- WebView2一覧化は将来検証候補に留める。
- Worker / sidecarは契約整備まで含めるが、IPC導入は実測後に判断する。
- 未コミット差分は戻さない。
- 実装は小Phase単位で行い、各変更でfocused test、Release x64 build、実機ログ、表示確認を閉じる。
