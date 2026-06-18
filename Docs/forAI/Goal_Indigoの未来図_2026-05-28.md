# Goal Indigoの未来図 2026-05-28

最終更新日: 2026-05-28

変更概要:
- IndigoMovieManager の将来像を、個別高速化計画より上位の「未来図」として明文化した。
- 既存コードを捨てず、WhiteBrowser互換と `.wb` 非破壊を守りながら、内部の背骨を段階的に差し替える方針を固定した。
- `maimai_MovieAssetManager` から取り入れるべき設計思想と、そのまま持ち込んではいけない実装癖を分けて整理した。
- サブエージェント批判役・調整役レビューを受け、非目標、Diff契約、Scheduler安全条件、maimai思想の変換ルール、ログ契約を追記した。

## 1. この文書の目的

この文書は、IndigoMovieManager が最終的にどのようなアーキテクチャへ進むべきかを固定する未来図である。

対象は、UI高速化だけではない。WhiteBrowser互換、`.wb` DB非破壊、一覧、検索、Watcher、サムネイル、Player、skin、救済処理、観測ログを含めたアプリ全体の進化先を扱う。

この文書は日々の作業手順ではなく、実装判断で迷った時に戻るべき設計上の正本である。

### 1.1 この文書の位置づけ

この文書は、`Goal_UI分離とスムーズ表示アーキテクチャ_2026-05-27.md` を包含する上位ゴールである。

ただし、日々の実装順は `Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md` を優先する。未来図は判断基準、Implementation Plan は着手順、UI分離GoalはUI詰まり対策の具体ゴールとして併読する。

`maimai_MovieAssetManager` の大規模動画ライブラリ注意点は、Indigoへそのまま移植する仕様ではなく、設計上の失敗を避けるための参考制約として扱う。

## 2. 結論

Indigo は丸ごと書き直さない。

既存コードを仕様の正本として扱い、動作互換を守りながら、内部に新しい背骨を通す。

目指す姿は次である。

- UI は入力、選択、スクロール、表示差分適用だけを担当する。
- 正本処理は Application Core と ReadModel Pipeline へ移す。
- `.wb` は変更しない。
- WhiteBrowser互換を壊さない。
- Watcher、thumbnail、skin、Player は UI を直接押し込まず、Scheduler 経由で要求を流す。
- 一覧更新は full reload ではなく diff-first を通常経路にする。
- 重い media compute と救済処理は worker / sidecar 化できる契約へ寄せる。
- 主要な詰まりは `debug-runtime.log` と必要な補助計測で分類できる状態にする。

### 2.1 この未来図でやらないこと

未来図は大改造の許可証ではない。次は非目標として固定する。

- 本体一覧UIの即時 WebView2 化。
- IPC / sidecar の先行導入。
- `.wb` スキーマ変更。
- `MainWindow` 全面置換。
- 検索仕様の別物化。
- 既存skin APIの破壊。
- 互換仕様を読まずに maimai のコード構造を移植すること。

PM判断として、当面の本線は WPF 一覧を維持した diff-first 化である。WebView2 一覧化は将来候補に留め、別検証なしに本線へ入れない。

## 3. Indigo が守る宇宙

Indigo の価値は、単なる動画管理UIではない。

守るもの:
- WhiteBrowser互換プログラムであること。
- WhiteBrowserのDB `*.wb` を変更しないこと。
- 既存ユーザーのDB、skin、サムネイル、bookmark、タグ、検索感覚を壊さないこと。
- 難読動画や失敗動画への救済導線を、通常動画テンポを壊さず維持すること。
- 大DBでも、最初に触れる一覧、検索、ページ移動、Player操作を軽く保つこと。

未来図は、これらを捨てるためのものではない。むしろ、守り続けるために内部構造を強くするためのものだ。

### 3.1 正本を分ける

「正本処理」という言葉を曖昧に使わない。未来図では、正本を次のように分ける。

- DB source: `.wb` と互換DBの永続正本。変更しない。
- Domain state: Indigo が解釈した動画、タグ、bookmark、skin、Player、救済状態。
- View ReadModel: UI表示に必要な並び、絞り込み、表示用状態。
- UI visual state: 選択、スクロール位置、viewport、フォーカスなど画面上の一時状態。

UI は visual state だけを持つ。DB source、Domain state、View ReadModel の正本判断は UI に戻さない。

## 4. 現在の問題の見立て

現在の詰まりは、処理単体が遅いだけではない。

根本は、UIスレッドと `MainWindow` 周辺に次の責務が集まりすぎていることである。

- 一覧の正本管理
- DB読込とDB保存
- 検索、sort、filter
- Watcher結果の反映
- thumbnailパス解決と画像供給
- skin refresh、catalog、profile保存
- Player起動、存在確認、再生統計保存
- 下部タブ、上部タブ、詳細表示、救済導線の更新

この形では、個別処理を少しずつ背景化しても、最後に `Refresh()` / `Items.Refresh()` / `FilterAndSort(..., true)` へ戻ると体感改善が相殺される。

したがって未来図では、UIを軽くするだけでなく、UIが正本処理を持たない構造へ変える。

## 5. 未来アーキテクチャ

### 5.1 UI Shell

UI Shell は、WPF の画面とユーザー入力の受け口である。

担当すること:
- 入力、選択、スクロール、タブ切り替えを受ける。
- 現在の ReadModel snapshot を描画する。
- Core から届いた小さな diff を適用する。
- 表示中範囲、選択中項目、ユーザー操作優先度を Scheduler へ伝える。

担当しないこと:
- DB全件読込。
- ファイル存在確認。
- 画像decode。
- 大件数検索、sort、filter。
- Watcher scan の本体処理。
- skin catalog の同期走査。
- thumbnail救済やmedia probe。

### 5.2 Application Core

Application Core は、Indigo の正本判断を持つ。

担当すること:
- `.wb` 互換の意味論を守る。
- DB、Watcher、thumbnail、skin、Playerから来る要求を正規化する。
- 操作を command と query に分ける。
- UIへ戻す結果を revision 付き snapshot / diff にする。
- 古い処理結果を UI へ反映しない。

Core は UI control を知らない。`MainWindow` も Core の内部状態を直接持たない。

Core は次の型や概念を参照しない。

- `Dispatcher`
- WPF `Control`
- `ObservableCollection`
- ViewModel
- WebView2 DOM 状態

Core が公開してよいのは、Command、Query、Event、Snapshot、Diff DTO である。

また、Core は次の巨大クラスになってはいけない。`MainWindow` の責務をそのまま Core へ丸移しするのは失敗である。Core は薄い調停層に留め、検索、ReadModel、永続化、画像、skin、worker は機能別 service へ分ける。

### 5.3 ReadModel Pipeline

ReadModel Pipeline は、一覧表示用の読み取りモデルを作る。

役割:
- `MovieRecords` の互換表現と、UI表示用 ReadModel を分ける。
- 検索、sort、filter を背景で実行する。
- 結果に source revision、query revision、sort revision を持たせる。
- UIへ全件再代入ではなく、追加、削除、更新、移動の diff を返す。

通常経路:
- 小差分は diff apply。
- query変更とsort変更は background recompute。
- DB切替と大量変更だけ full snapshot reload。
- 古い recompute は cancellation / revision guard で捨てる。

Diff は最低限、次を持つ。

- stable key
- source revision
- view revision
- operation
- affected sort key
- affected query fields
- selection impact
- scroll / focus impact
- fallback reason

Diff 適用できる条件:
- stable key が一致している。
- source revision が現在のDBと一致している。
- query / sort 条件が要求時から変わっていない。
- dirty field が現在の検索条件やsort keyへ影響しない、または影響を判定できる。
- 削除後の選択、フォーカス、表示位置の扱いを決められる。

Full fallback を許す条件:
- DB切替。
- 初期完全読込。
- query条件変更。
- sort key変更。
- 大量変更しきい値超過。
- `{dup}` や hash 系など、集合全体の意味が変わる変更。
- dirty field の影響が判定不能な変更。

これ以外の full reload は、許容 fallback か局所反映化対象かをログに残して分類する。

### 5.4 Scheduler

Scheduler は、どの要求を先に通し、何を後ろへ送るかを決める。

優先順位:
1. 入力、スクロール、選択、タブ切り替え
2. Playerの明示操作
3. 現在表示中の一覧差分
4. visible範囲の画像供給
5. 最新の検索、sort、filter
6. Watcherの小差分反映
7. thumbnail生成、rescue、prewarm
8. skin catalog再確認、profile保存
9. background repair、広域再走査

重要なのは、全部を速くすることではない。ユーザー操作が来た時に、背後処理が譲ることである。

Scheduler の実装契約:
- queue は bounded にする。
- 同一 key の古い要求は coalesce する。
- 検索、sort、detail、画像要求は latest-only を基本にする。
- user priority には開始、解除、timeout、release reason のログを必ず残す。
- background full reload は user priority 中に積まない。
- starvation 防止のため、background は完全停止ではなく延期・間引き・再開可能にする。
- shutdown は `complete -> bounded drain -> timeout log` の順にする。
- cancel できない running job は、UI反映だけでも revision guard で捨てる。

### 5.5 Image Pipeline

Image Pipeline は、サムネイルと画像表示を visible-first にする。

担当すること:
- visible範囲を最優先にする。
- off-screen decode を入力やスクロールより優先しない。
- 画像存在確認、stamp取得、decodeをUIスレッドから外す。
- cache miss は placeholder で先に返す。
- 成功、未生成、ERROR marker、FailureDb由来の状態を revision 付きで反映する。

禁止:
- UI上で行ごとに `File.Exists(...)` を呼ぶ。
- 画像bytesをUI更新経路へ巨大payloadとして流す。
- サムネ成功後に一覧全体 reload へ戻る。

### 5.6 Watch Pipeline

Watcher は、ファイル変更を検知する入口であり、UI更新の実行者ではない。

担当すること:
- FileSystemWatcher / Everything / Manual scan の入力を change set へ正規化する。
- `ChangeKind`、`DirtyFields`、`ObservedState` を明示する。
- 小差分で済むものは ReadModel Pipeline へ diff として渡す。
- unsafe dirty、大量変更、DB切替だけ full fallback へ落とす。

禁止:
- Watcher終端で無条件に `FilterAndSort(..., true)` を呼ぶ。
- user priority 中に full reload を押し込む。
- DB切替後着の古いWatch結果をUIへ反映する。

### 5.7 Persistence Pipeline

保存処理は、UI操作から分離する。

対象:
- 設定保存
- tag変更
- score / view_count / last_date 更新
- bookmark追加、削除
- skin profile保存
- movie_path更新
- queue / failure / rescue の状態保存

原則:
- UIは表示値を先に更新する。
- 保存は背景で直列化する。
- shutdown時だけ bounded drain する。
- 保存失敗はログと必要最小限の通知で扱う。

楽観UI更新を行う場合、失敗時の一貫性を必ず設計する。

- dirty
- saving
- persisted
- failed
- retryable

これらを区別し、UI表示値とDB永続値が一時的にずれている時も、ログで追えるようにする。

### 5.8 Skin Runtime

skin は、表示切替、catalog、profile保存、WebView navigation を分ける。

担当を分ける:
- current skin 解決
- catalog refresh
- stale判定
- host prepare
- navigation
- profile persist
- external skin API snapshot

完了条件:
- skin切り替え1回で不要な refresh が重ならない。
- catalog再走査が常時発生しない。
- DB write がUI同期ボトルネックとして残らない。
- `refresh end` のログで catalog / persist / navigate / stale の内訳を説明できる。

### 5.9 Player Surface

Player は、一覧やWatcherの背後処理から守る。

原則:
- Player起動時のファイル存在確認は背景へ逃がす。
- 再生統計保存は背景へ逃がす。
- 音量や再生状態の保存は debounce + background queue にする。
- Player操作中は user priority を張り、watch / thumbnail / poll を後ろへ送る。
- WebView Player の surface 再利用を優先し、不要な reload を避ける。

### 5.10 Worker / Sidecar

重い media compute は、将来的に worker / sidecar へ逃がせる契約にする。

対象候補:
- thumbnail生成
- rescue / repair
- metadata probe
- 難読動画の解析
- 大量hash計算
- 外部ツール呼び出し

原則:
- worker は WPF control を知らない。
- worker は UI状態を知らない。
- worker は必要最小限の入力と出力だけを持つ。
- 結果は file / manifest / DB command で返す。
- 失敗はアプリ本体を巻き込まない。

当面は、同一プロセス内でも worker 契約と入出力DTOを先に固定する。IPCや別プロセス化は、UI詰まりの主因をログで切り分けた後に判断する。

## 6. `maimai_MovieAssetManager` から取り入れるもの

`maimai_MovieAssetManager` は、Indigoの未来図を考えるうえで良い参考になる。

取り入れる思想:
- `Core / Application / Infrastructure / Host UI` の層分離。
- `WPF shell + WebView2` の役割分担。
- custom scheme + stream による画像搬送。
- Base64画像搬送を避ける方針。
- visible / prefetch の優先queue。
- workerがSQLite、WebView2、WPF ViewModelを知らない契約。
- 曖昧な判定を `NeedsReview` 相当へ逃がす考え方。

そのまま持ち込まないもの:
- 本体一覧UIの即時 WebView2 化。
- UIコマンド内の同期DB query。
- selection変更ごとの重いdetail同期読込。
- ViewModel内の全件 `Clear()` / 全追加。
- bootstrap payloadの肥大化。
- v1作業台として太い `MainWindow` / ViewModel 構造。

結論として、maimai のコードを移植するのではない。maimai の設計思想を、Indigo の互換仕様と現実の運用負荷へ合わせて再設計する。

### 6.1 Indigoへ持ち込む時の変換ルール

maimai の思想を Indigo へ持ち込む時は、次の変換を必ず通す。

- `.wb` は変更しない。
- WPF一覧を当面の本線として維持する。
- WebView2化は将来候補であり、別検証が必要な大粒度変更として扱う。
- 同期DB query は UI 入口に置かない。
- 全件 `Clear()` / 全追加を検索入力やwatch反映の通常経路にしない。
- 補助indexやcacheを使う場合は、`%LOCALAPPDATA%` などの再生成可能領域に限定する。
- 補助indexやcacheは `.wb` の正本ではなく、壊れたら破棄して再構築できるものにする。
- Base64画像搬送は禁止する。
- UI上の行単位 `File.Exists(...)` は禁止する。
- detail読込のN+1 queryはログとテストで検出できるようにする。

## 7. 移行戦略

未来図への移行は、一括置換ではなく Strangler 型で進める。

### Phase A: 現行hot pathの分類

目的:
- `Refresh()` / `Items.Refresh()` / `FilterAndSort(..., true)` を全件棚卸しする。
- 許容 fallback と局所反映化対象を分ける。
- UI上の同期DB / file I/O / image decode を入口別に分類する。

完了条件:
- 残る全面再評価の理由がログとコードで説明できる。

### Phase B: ReadModel境界の新設

目的:
- `MovieRecords` とUI表示用 ReadModel を分ける。
- 一覧更新を ID ベースの diff apply へ寄せる。
- search / sort / watch の結果を同じ差分形式へ寄せる。

完了条件:
- Watch 1件追加、rename、tag変更、thumbnail成功が通常経路で full reload へ戻らない。

### Phase C: Scheduler導入

目的:
- user priority、visible priority、background priority を1箇所で扱う。
- Watcher、thumbnail、skin、Playerが個別にUIへ割り込まないようにする。

完了条件:
- 検索やPlayer操作中に、watch / thumbnail / poll が完了を妨げない。

### Phase D: Image Pipeline統一

目的:
- 上側タブ、下側タブ、詳細、Player右レールの画像供給を同じ visible-first 原則へ寄せる。

完了条件:
- off-screen decode が visible表示や入力を押しのけない。

### Phase E: Skin / Player / WatcherのCore接続

目的:
- skin、Player、Watcher が直接UI更新を持たず、Core / Scheduler / ReadModel 経由へ寄る。

完了条件:
- 各機能の完了ログが、UI詰まり原因と分離して読める。

### Phase F: Worker / Sidecar境界の固定

目的:
- thumbnail、rescue、metadata probe を本体UIからさらに切り離す。

完了条件:
- worker失敗やtimeoutが本体UIを巻き込まない。

### 7.1 現行Laneとの対応

未来図の Phase は、現行 Implementation Plan の Lane と次のように対応させる。

| 未来図 | 対応する現行Lane | 意味 |
| --- | --- | --- |
| Phase A | Lane 0 / Lane 1 | 計測固定、UIスレッド簡素化、hot path分類 |
| Phase B | Lane 1 / Lane 2 | diff-first 一覧更新、ReadModel境界 |
| Phase C | Lane 1 / Lane 3 | 入力優先、watcher / poll / shutdown 境界 |
| Phase D | Lane 5 | visible-first / Player / 画像供給 |
| Phase E | Lane 3 / Lane 6 | Watcher、skin、Playerの境界整理 |
| Phase F | Lane 7 / 後段 | rescue / repair 維持、worker / sidecar 境界 |

### 7.2 既存コードからの対応表

現行コードを読む時は、次の移行先を意識する。

| 現行の主な処理 | 未来図での行き先 |
| --- | --- |
| `FilterAndSortAsync(...)` | ReadModel Pipeline |
| `RefreshMovieViewFromCurrentSourceAsync(...)` | Diff applicator / ReadModel反映 |
| Watcher終端 reload 判断 | Watch Pipeline / Scheduler |
| サムネイル成功通知 | Image Pipeline |
| 下部 ERROR / 進捗 snapshot | Image Pipeline / Scheduler |
| 設定保存、score、tag、bookmark保存 | Persistence Pipeline |
| skin refresh / catalog / persist | Skin Runtime |
| Player起動と統計保存 | Player Surface / Persistence Pipeline |
| rescue / repair / metadata probe | Worker / Sidecar 契約 |

## 8. 禁止線

未来図に反するため、次は原則禁止する。

- `.wb` スキーマを変更する。
- WhiteBrowser互換を壊す。
- UIテンポ改善を理由に観測ログを削る。
- `MainWindow` へ新しい正本責務を戻す。
- 背景化した結果を最後に全面 `Refresh()` で相殺する。
- 別プロセス化だけでUI詰まりが解決したことにする。
- 画像bytesを巨大payloadとしてUIへ流す。
- user priority中にbackground full reloadを押し込む。
- 難読動画対応を通常動画hot pathへ混ぜる。
- Base64画像搬送を導入する。
- UI上で行単位 `File.Exists(...)` を復活させる。
- 補助indexやcacheを `.wb` の代替正本にする。

## 9. 受け入れ基準

未来図に近づいたと判断できる条件は次である。

- 起動は `first-page shown`、`input ready`、`heavy services started` に分けて説明できる。
- 入力、スクロール、選択、タブ切り替えが Watcher / thumbnail / skin / DB保存に押し負けない。
- 大件数検索やsortで、古い処理が後着入力後もCPUを食い続けない。
- UIへ戻る変更が revision付き diff として説明できる。
- visible範囲外の画像decodeが visible範囲を押しのけない。
- skin切り替えの重さを catalog / persist / navigate / stale に分けて説明できる。
- Player操作中の背後処理抑止と解除がログで追える。
- `debug-runtime.log` だけで、詰まり原因を search / sort / watch / image / skin / persist / player / worker に分類できる。
- `first-page shown` / `input ready` / `heavy services started` が同一 revision / trigger / elapsed_ms で追える。
- 後着処理は `sort canceled` / `stale skip` / `revision-stale` として破棄理由が残る。
- watch full fallback は reason を持つ。
- skin は `refresh end` と `catalog_*` / `persist_*` / `navigate_*` / `stale` を同じ trace で追える。
- visible画像要求は request から ready / missing / canceled まで追える。
- UI publish は item count / payload size / elapsed_ms を必要に応じて記録できる。

## 10. ログ契約

未来図に沿う処理は、少なくとも次の項目をログへ残せるようにする。

- request_id
- source_revision
- view_revision
- db_path または DB identity
- trigger
- reason
- skip_reason
- elapsed_ms
- item_count
- changed_count
- queue_depth
- priority
- cancellation reason
- fallback reason

ログは高速化の飾りではない。未来図では、ログで説明できない改善は完了扱いにしない。

## 11. 用語定義

- diff-first: 全面再評価ではなく、追加、削除、更新、移動の差分反映を通常経路にする方針。
- 全面再評価: 現在のsourceから検索、sort、表示collectionを広く作り直す処理。
- full fallback: diffでは安全に反映できないため、意図的に全面再評価へ戻ること。
- ReadModel: UI表示に必要な読み取り専用の表示モデル。
- user priority: 検索、選択、Player操作など、ユーザー明示操作を背後処理より優先する状態。
- visible-first: 現在見えている範囲の画像や表示更新を最優先する方針。
- sidecar: 本体とは別の実行境界で重い処理や危険な処理を担当する補助プロセス。

## 12. AIへの指示

Indigoを触るAIは、作業前に次を自問する。

- この変更は未来図のどの層を前に進めるのか。
- UIスレッドへ新しい正本責務を戻していないか。
- `Refresh()` / `Items.Refresh()` / `FilterAndSort(..., true)` を増やしていないか。
- `.wb` 非破壊とWhiteBrowser互換を守っているか。
- 通常動画のテンポを難読動画対応で壊していないか。
- ログだけで、速くなった理由または遅くなった理由を説明できるか。

判断に迷ったら、局所最適ではなく、ReadModel、Scheduler、Image Pipeline、Worker境界のどれへ寄せるべきかを先に決める。

## 13. 関連資料

- `%USERPROFILE%\source\repos\IndigoMovieManager\Docs\forAI\Goal_UI分離とスムーズ表示アーキテクチャ_2026-05-27.md`
- `%USERPROFILE%\source\repos\IndigoMovieManager\Docs\forAI\Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md`
- `%USERPROFILE%\source\repos\IndigoMovieManager\Docs\forAI\Implementation Plan_長期ロードマップ_体感高速化UI分離_Worker契約_2026-06-18.md`
- `%USERPROFILE%\source\repos\IndigoMovieManager\AI向け_現在の全体プラン_workthree_2026-03-20.md`
- `%USERPROFILE%\source\repos\IndigoMovieManager\AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md`
- `%USERPROFILE%\source\repos\IndigoMovieManager\WhiteBrowserSkin\Docs\Implementation Plan_skin切り替え高速化_DB保存分離先行_2026-04-13.md`
- `%USERPROFILE%\source\repos\maimai_MovieAssetManager\Docs\大規模動画ライブラリ注意点_2026-05-27.md`
