# Implementation Plan UIを含む高速化のための抜本改善プラン 2026-04-17

最終更新日: 2026-05-27

変更概要:
- 2026-05-27 のサブ5.5 Worker D で、Everything poll policy の DB path 存在確認を watch folder と同じ `pathExists` delegate 経由へ統一した。instance 側で確認した DB 存在結果を snapshot 取得と policy 判定へ引き継ぎ、DB / watch folder 大件数時に同じ DB path の filesystem probe を重ねない形へ寄せた。
- 2026-05-27 のサブ5.5 Worker E で、Thumbnail ERROR タブの手動再読込、一覧クリア、選択/一括救済投入後に残っていた `Refresh()` 全体再描画を外し、`RefreshThumbnailErrorRecords(force: true)`、上側 visible range refresh、下部進捗 snapshot 予約へ寄せた。救済投入 core から UI 更新を外し、背景投入完了後に UI 側で1回だけ局所反映する。
- 2026-05-27 のサブ5.5 Worker C で、起動 light services の EverythingLite watch root prewarm は UI 側で DB / provider / revision の snapshot だけを取り、watch root の `Path.Exists` を含む plan 作成を `Task.Run` 背景 helper へ逃がした。戻り時は startup revision / 現在 DB / root snapshot を guard し、古い起動要求や DB 切替後着の root prewarm を捨てる。
- 2026-05-27 のサブ5.5 Worker A追加で、起動 `ContentRendered` 直後の `AutoOpen` / `LastDoc` 自動DB切替は設定値を snapshot してから `Task.Run` の存在確認へ逃がし、UI 復帰後に `AutoOpen` / `LastDoc` / window closing / dispatcher shutdown guard を通した時だけ `TrySwitchMainDb(..., StartupAutoOpen)` を実行する形へ寄せた。
- 2026-05-27 のサブ5.5 Worker B 追加で、新規DB作成ダイアログ後の `Path.Exists(...)` と `TryCreateDatabase(...)` を path snapshot 後の `Task.Run` helper へ逃がし、ダイアログ表示、既存ファイル警告、作成失敗表示、`TrySwitchMainDb(...)` は UI 側に残した。watch folder drop から空DB状態で新規作成へ進む経路も async 化し、作成完了後は DB 切替が途中で起きていない時だけ新DBへ切り替える。
- 2026-05-27 のサブ5.5追加で、単発 `DataRowToViewData(...)` の表示用レコード生成を背景 `Task.Run` + `MovieRecordBulkBuildCache` 経路へ寄せ、サムネ候補探索と動画本体 `Path.Exists` を UI スレッドから外した。watch 経由の単発追加は DB snapshot 一致時だけ UI へ反映し、追加後の動画存在状態は `QueueMovieExistsRefresh([item], revision)` で後追い反映する。
- 2026-05-27 のサブ5.5追加で、通常の動画削除確定後に残っていた `DeleteMovieTable(...)` / `TryDeletePhysicalFile(...)` / サムネイル探索削除を選択行 snapshot 後の `Task.Run` helper へ逃がし、失敗表示と局所削除反映だけを DB 一致 guard 後に UI へ戻す形にした。
- 2026-05-27 のサブ5.5追加で、外部 skin サムネ契約生成の `CacheOnly` 経路は、既存サムネパス文字列・placeholder/error 文字列判定・既存サイズ/revisionキャッシュだけで応答し、`Path.Exists` / ファイルスタンプ / WB メタ読み / 画像 decode へ進まない軽量経路へ寄せた。未キャッシュ時だけ未確定寸法と revision を安全値へ縮退し、後続 `FullSync` / 更新 callback で正確化する。
- 2026-05-27 のサブ5.5追加で、Debug タブの現在DB / FailureDB / QueueDB ファイル削除に残っていた `File.Exists` / `File.Delete` を確認後の `Task.Run` helper へ逃がし、削除失敗時の復旧用存在確認と `LastDoc` 保存も UI 同期 I/O から外した。
- 2026-05-27 のサブ5.5追加で、外部 skin host prepare の HTML 存在確認と WebView2 userDataFolder 作成を `Task.Run` helper へ逃がし、戻った後の stale guard と navigate / WebView2 操作の UI 側責務を維持した。fallback log ボタンも Explorer 引数判定だけを背景化し、`Process.Start` は UI 側に残した。
- 2026-05-27 のサブ5.5追加で、設定画面の skin selector 初期化 / Activated 更新に残っていた同期 `GetAvailableSkinDefinitions()` を async 一覧取得へ寄せ、catalog load は `Task.Run` 背景実行、UI 反映は revision guard 後に限定した。
- 2026-05-27 のサブ5.5追加で、DuplicateVideos タブのグループ一覧生成時に残っていた代表サムネ `File.Exists` を左ペイン VM 生成ごと `Task.Run` へ逃がし、後着 group revision guard 後に UI へ反映する形にした。
- 2026-05-26 のサブ5.5追加で、Debug タブのExplorer起動前に残っていた `File.Exists` / `Directory.Exists` を path snapshot 後の `Task.Run` helper へ逃がし、Explorer起動と未存在メッセージだけを revision / shutdown guard 後に UI へ戻す形にした。
- 2026-05-26 のサブ5.5追加で、Explorer drag/drop の .wb 判定に残っていた `File.Exists` を DragOver から外し、Drop 確定後の `Task.Run` 存在確認と DB 一致/shutdown guard へ分離した。
- 2026-05-26 のサブ5.5追加で、メニューの「親フォルダを開く」に残っていた `Path.Exists(mv.Movie_Path)` / `Path.Exists(mv.Dir)` を選択パス snapshot 後の `Task.Run` helper へ逃がし、ネットワークパス確認中も UI スレッドの入力/描画を塞ぎにくくした。
- 2026-05-26 のサブ5.5追加で、Log タブ preview 更新に残っていた `File.Exists` / `File.GetLastWriteTimeUtc` / 末尾 preview 読みを `Task.Run` helper へ逃がし、後着 request id guard で古い preview が最新表示を上書きしないようにした。
- 2026-05-26 のサブ5.5追加で、ExtDetail の explorer 選択リンクと詳細サムネ watcher 後段に残っていた `Path.Exists(...)` を snapshot 後の `Task.Run` helper へ寄せ、クリックや watcher 反映時に UI スレッドでファイル存在確認を掘らない形へ寄せた。
- 2026-05-26 のサブ5.5追加で、ExtDetail の右クリック詳細サムネ存在確認と watcher 初期設定時の `Path.Exists(...)` / `Directory.Exists(...)` も背景 helper へ逃がし、record/path/revision 一致時だけ画像再評価と監視作成を UI へ戻す形にした。
- 2026-05-26 のサブ5.5追加で、詳細サムネ表示モード切替と Log タブ debug カテゴリ切替に残っていた `Properties.Settings.Default.Save()` 直呼びを `QueueApplicationSettingsSave(...)` へ寄せ、UI 操作中の設定ファイル I/O を共通の背景保存キューへ逃がした。
- 2026-05-26 のサブ5.5追加で、Thumbnail 成功後の main tab 後段 `FilterAndSort(sortId, true)` を廃止し、失敗キャッシュ無効化、上側タブ visible refresh、preferred key revision、下部 ERROR/進捗 snapshot 予約へ寄せた。サムネERROR順だけは順序が変わるため、DB 再読込ではなく現在一覧の `SortDataAsync("28")` に限定する。
- 2026-05-26 のサブ5.5追加で、Player 通常再生入口の `Path.Exists(mv.Movie_Path)` を選択パス snapshot 後の `Task.Run` helper へ逃がし、存在確認中も UI スレッドの入力/描画を塞ぎにくくした。
- 2026-05-27 のサブ5.5追加で、Player タブ表示/切替入口の `Path.Exists(movie.Movie_Path)` も path snapshot 後の `Task.Run` helper へ逃がし、存在しない時は `user-priority` を開始せず戻る形にした。
- 2026-05-26 のサブ5.5追加で、Debug タブの「現在サムネイルを削除」に残っていた `Directory.Exists` / `Directory.Delete(..., true)` を `Task.Run` へ逃がし、削除中も UI スレッドの入力/描画を塞ぎにくくした。
- 2026-05-27 のサブ5.5追加で、Debug タブの現在DB / QueueDB / FailureDB レコード件数取得に残っていた `File.Exists` と SQLite read を path snapshot 後の `Task.Run` helper へ逃がし、revision / path / shutdown guard 後に結果だけ UI へ戻す形にした。
- 2026-05-26 のサブ5.5追加で、起動 fallback と段階ロード中 sort 変更の `FilterAndSort(..., true)` は DB 正本復旧・全件順序復旧の許容 fallback として分類し、Debug タブのサムネイル全削除後 `FilterAndSort(..., true)` は表示モデルのサムネパスクリア、visible refresh、進捗/ERROR snapshot 予約へ置き換えた。
- 2026-05-26 のサブ5.5で、外部 skin API の sort は通常時 `SortDataAsync(...)` を await する経路へ寄せ、`FilterAndSort(resolvedSortId, true)` の全件 reload fallback を通常経路から外した。起動 partial feed 中だけは全件順序の正しさを守るため `partial-feed-needs-complete-source` として分類ログを残し、startup feed をキャンセルしてから正規の後着キャンセル付き reload へ戻す。
- 2026-05-26 のサブ5.5で、Bookmark タブの `Items.Refresh()` は `ObservableCollection` 通知へ任せる形で撤去し、ExtDetail / ExtensionDetail のタグ再描画は表示中の view-local 更新だけへ絞った。
- 2026-05-26 の `73b96e4` で、skin `refresh end` を early skip でも必ず出し、`elapsed_ms` に加えて `catalog_*` / `persist_*` / `navigate_*` / `refresh_*_skipped` を同一 payload で出す形へ寄せた。skin 完了判定は DB 分離ではなく、catalog / persist / navigate / stale の内訳を実機ログで説明できることを引き続き基準にする。
- 2026-05-26 の `0992877` で、起動 warm path の `first-page shown` / `input ready` / `heavy services started` を同一 revision / trigger / elapsed_ms で追えるようにし、partial-feed / fallback / complete / canceled を `feed_state` で区別できるようにした。
- 2026-05-26 の `6bb00e5` で、大件数時の通常 `SortData(...)` を background + revision guard へ寄せ、後着 sort は `sort canceled` / `sort skip stale` で破棄できるようにした。
- 2026-05-26 の `5a90210` で、`TagControl` / `MainWindow.Tag` のタグ編集後 `Refresh()` / `Items.Refresh()` と、`KanaBackfill` のかなソート時 `FilterAndSort(..., true)` を DB 再読込なしの in-memory 局所反映へ寄せた。
- 2026-05-26 の `7b34692` で、サムネイルのみ削除後の `FilterAndSort(..., true)` を廃止し、対象 `MovieRecords` のサムネ表示パスクリア、上側タブ visible refresh、下部 ERROR/進捗 snapshot 予約へ寄せた。
- 2026-05-26 の `cc1b094` で、動画削除後の `FilterAndSort(..., true)` を廃止し、DB 削除成功行だけを `MovieRecs` / `FilteredMovieRecs` から ID ベースで差分削除し、SearchCount / visible refresh / ERROR・進捗 snapshot 予約へ寄せた。
- 2026-05-26 の `8f7f8ff` で、viewport が一時的に計測不能でも、同一タブかつ同一 source revision の場合は直前の preferred key snapshot を保持し、不要な revision 更新と全画像再評価を増やさないようにした。
- 2026-05-26 の `218ff91` で、watch / rename / query-only の in-memory refresh に後着キャンセル token を通し、古い再検索・再整列が UI へ戻らないようにした。
- 2026-05-26 の見直しで、計画の軸は維持しつつ、次の主戦場を「watch / rename の in-memory refresh 後着キャンセル」「残る Refresh() / Items.Refresh() / FilterAndSort(true) の局所反映化」「大件数 sort の background + revision guard」「実機ログでの起動 / skin / visible-first 完了判定」へ絞った。
- 進捗は実装ベース約 88%、実機確認込み 76〜78% として扱う。完了扱いは、debug-runtime.log と実機操作で支配要因を説明できる状態まで保留する。
- 大件数検索では、後着検索が入った時に古い `FilterAndSortAsync(...)` の `filter-movies` 列挙を cancellation token で中断できるようにし、入力中の古い全件検索が CPU を食い続ける状態を減らした
- 大件数の ASCII 検索では `Movie_Name / Movie_Path / Tags / Comment1-3 / 既存 Roma / 既存 Kana 由来 Roma` の軽量投影に留め、`Kana/Roma` が空の行で名前/パスから読み仮名解析へ戻る fallback を UI hot path から外した
- watch 終端 reload は `changedMovies` が no-op 札だけなら実効変更なしとして扱い、deferred/full reload を積まないようにした
- Rescue タブの通常再試行 / 黒背景救済 retry / index repair dispatch は、投入または開始が 0 件の時は下部 ERROR/進捗 snapshot 予約も行わず、操作後の空振り再評価を減らした
- 重複動画タブは名前変更を `PropertyChanged` 通知で反映し、`Items.Refresh()` に戻らない形へ寄せた。さらに左グループ選択時の右ペイン詳細生成はサムネ存在確認を含めて背景化し、選択 revision 一致時だけ UI へ戻すようにした
- 外部 skin API のタグ変更後は `RefreshViewsAfterTagEditorRecordChange(...)` の局所反映に留め、追加の `Refresh()` による一覧全体再描画へ戻らないようにした
- 検索履歴保存/再読込、watch フォルダ drop 反映、Rescue 履歴反映、サムネ進捗の初期作成数反映は、`ContinueWith` / `task.Result` ではなく async helper へ寄せ、DB/ファイル I/O 後の UI 反映を `DispatcherPriority.Background` と後着 guard で行う形に揃えた
- watch の bulk full fallback 理由を unsafe dirty / unsafe change kind まで細分化し、既存行の安全差分に no-op 札が混ざるだけなら query-only 復帰できるようにした
- サムネ進捗の enqueue / 初期作成数反映は runtime version 差分 guard 経由へ寄せ、状態変化がない時の余分な snapshot 予約を減らした
- 外部 skin refresh は `CatalogRefresh` / `CachedSnapshot` を明示し、`minimal-chrome-reload` は cached definition に閉じ、明示 reload / fallback retry だけ catalog 再確認を async 経路へ通す形にした
- 詳細サムネ側の表示更新は、UI 上で既存 jpg 走査、ERROR marker 確認、FailureDb open rescue 確認、未生成通常キュー投入、ERROR 救済投入を直接行わず、選択行と DB 情報を snapshot に固定して背景確認し、DB 一致・選択一致・shutdown guard 後に `ThumbDetail` と下部 ERROR/進捗 snapshot 更新だけ戻す形へ寄せた
- 上側タブの可視 ERROR 自動投入は UI 上で救済投入入口を直接回さず、可視 `MovieRecords` を DB 情報付き snapshot に固めてから、marker 削除 / FailureDb 確認 / queue 投入を背景側で実行する形へ寄せた。背景 core でも `AreSameMainDbPath` guard を通し、DB 切替後着と shutdown 中の要求は捨てる
- `TryEnqueueThumbnailDisplayErrorRescueJob` と下部 `ThumbnailError` 可視行優先投入は、通常キューへ戻す可能性がある `tab-error-placeholder` の時だけ FailureDb 履歴を読み、可視行自動投入などでは不要な履歴 read を避けるようにした
- 下部 `ThumbnailError` の一覧クリアは UI クリック処理で marker 削除 / FailureDb 削除を直接行わず、対象 snapshot を背景削除へ渡して DB 一致 guard 後に一覧更新だけ戻す形へ寄せた
- 下部 `ThumbnailError` の可視行優先投入は UI timer 上で marker 削除 / FailureDb 履歴確認 / queue 投入を直接実行せず、可視行 snapshot を背景 single-flight へ渡して完了後に DB 一致 guard 付きで snapshot 更新だけ戻す形へ寄せた
- 下部 `ThumbnailError` の 1 秒 timer は pending rescue 判定で FailureDb を UI 上から直接読まず、背景 single-flight の DB パス付き cache を参照する形へ寄せた
- 下部 `ThumbnailError` の snapshot 集計は FailureDbService 生成も背景側へ寄せ、DB 切替後着は `AreSameMainDbPath` guard で捨てるようにした
- `ThemeModeTests` / `ThumbnailProgressTabViewTests` は `Application.Current.Resources` と `ThemeMode` 設定値をテストごとに復元し、テーマ辞書の後続テスト汚染を抑えるようにした
- `ThumbnailProgressTabView` の単体生成テストは MaterialDesign 本体へ戻さず、軽量 `Indigo` profile 辞書をテスト中だけ適用して共通 style 解決を固定した
- 下部 `ThumbnailProgress` の救済workerカードは UI tick 上で FailureDb / `File.Exists` を読まず、背景 single-flight で作った cache を DB 一致 guard 後に反映する形へ寄せた
- Rescue タブの通常再試行 / 黒背景救済 retry / index repair dispatch 後は `Refresh()` で詳細再構築せず、下部エラー/進捗 snapshot 予約だけへ置き換えた
- preferred / manual rescue の即時サムネ成功で対象 `MovieRecords` へ直接反映済みなら、詳細再構築の `Refresh()` も省き、未反映時だけ保険として実行するようにした
- Rescue 一覧サムネは `NoLockImageConverter` へ decode height 18 を渡し、行サムネの cache miss 時にフルサイズ decode へ戻りにくくした
- preferred / manual rescue の即時サムネ成功で対象 `MovieRecords` へ直接反映済みなら、viewport 再計測ではなく preferred key revision だけで画像 Binding を再評価するようにした
- Rescue 履歴パネルは選択変更時に UI スレッドで FailureDb を同期読込せず、履歴読込と整形を背景へ逃がして revision 一致時だけ反映するようにした
- Created watch event の同一パス連続投入は ready 待ち pipeline で1本へ圧縮し、同じファイルのコピー待ちを重複して直列に積まないようにした
- 検索欄を空へ戻す導線も検索正本へ合流し、一覧反映と先頭選択を先に通してからサムネ常駐再起動へ進む順に揃えた
- rescued sync 完了時の `Refresh()` は、対象が選択中レコードへ反映された時だけに絞り、FailureDb / progress 更新は維持したまま一覧全体の再評価を減らすようにした
- fallback 起動でも `ThumbnailProgress` snapshot を直接更新せず、通常起動と同じ coalesce 済みの予約経路へ合流するようにした
- shutdown drain は watch queue / created pipeline に加えて check-folder queue runner も同じ500ms deadline内で待ち、終了時の走査残留をログ名付きで追えるようにした
- `ContentRendered` 直後の `ThumbnailProgress` snapshot 直更新を外し、first-page 後の startup light services から既存 coalesce 経路へ予約する形にした
- 検索確定時のサムネ常駐再起動は、検索結果反映と先頭選択の後へ送り、検索完了導線を先に通すようにした
- 通常上側タブ Small / Big / Grid / List / Big10 の画像 Binding も `Movie_Path` と preferred key revision を受け取り、off-screen 画像再評価を Player 右レールと同じ gate で抑えるようにした
- watch query-only 局所更新の changed path lookup は、`sourceMovies` / current filtered 全件を辞書化せず、変更対象 path だけを保持する形へ寄せた
- Created watch event の ready 待機は shutdown 開始後に短い分割待機で抜け、created detached pipeline が bounded drain 後に残り続けないようにした
- Player 右レールの `SelectionChanged` は抑止中・非表示中に詳細/タグ更新を走らせず、選択同期中の二重 UI 更新を避けるようにした
- preferred サムネ生成成功時、対象 `MovieRecords` へ直接反映できた場合は後段の `FilterAndSort(..., true)` を省き、forced rebind と visible refresh で止めるようにした
- Player 右レール / visible-first 画像供給では、preferred key 更新後に full reload へ戻さず、軽い revision / trigger で実現済み画像 Binding を再評価させる方針を固定した
- manual rescue 即時反映でも対象 `MovieRecords` へ直接反映できた場合は後段の `FilterAndSort(..., true)` を省き、forced rebind と visible refresh で止めるようにした
- `UiHang` native overlay thread の `Create -> Drain -> Run` を `try/finally` で包み、起動直後の例外や stop 競合でも native/fallback window を必ず破棄するようにした
- `UiHang` overlay thread の終了時クリアは thread / dispatcher の一致確認つきにし、Stop timeout 後の再 Start 状態を古い thread が消さないようにした
- `UiHang` overlay thread 内の例外は `overlay thread failed` としてログに閉じ、本体プロセスへ波及させないようにした
- `UiHang` overlay dispatcher action 内の未処理例外も `Handled=true` でログへ閉じ、補助通知の描画失敗が本体へ波及しないようにした
- Stop timeout 後に古い `UiHang` overlay thread がまだ alive の場合は再 Start をスキップし、共有 HWND 状態の二重所有を避けるようにした
- WebView Player の動画切り替え時に、ホスト側音量適用中の `volumechange` 通知と 100% 既定通知を保存しないようにし、ユーザー音量がリセットされる経路を塞いだ
- Bookmark 追加時の `MovieInfo` 生成、bookmark フォルダ作成、DB 登録を UI クリック処理から外し、サムネ生成成功後に背景 DB 登録する順へ寄せた
- Bookmark 削除時の DB 書き込みも UI クリック処理から外し、DB が同じ時だけ一覧 reload する形へ寄せた
- Player 再生開始後の score / view_count / last_date と Bookmark 再生回数更新を UI クリック処理から外し、背景保存へ寄せた
- Player のサムネイルシート再生位置解析と音量設定 `Save()` も UI クリック/スライダー処理から外し、解析は背景 task、設定保存は debounce 後の直列背景保存へ寄せた
- MainDB 切替・終了・Player fullscreen debug の `Settings.Save()` は共通の直列背景保存へ寄せ、終了時だけ短時間 drain して取りこぼしと UI 待ちを両立する形にした
- スコア増減メニューの DB 更新も UI クリック処理から外し、表示値を先に変えて DB 保存は背景へ逃がす形へ寄せた
- タグメニュー、下部タグ編集タブ、タグチップ削除の DB 更新も背景保存へ寄せ、タグ操作中の UI 待ちを減らした
- ファイル移動後の `movie_path` DB 更新も背景保存へ寄せ、移動後の表示反映と DB 保存待ちを分離した
- ファイルコピーは登録状態を変えないため、コピー I/O を背景化し、失敗だけ完了後に UI へ集約表示する形へ寄せた
- ファイルリネーム時の watcher 抑止も共通 helper へ寄せ、例外時でも `finally` で復旧するようにした
- サムネイルのみ削除は DB を触らないファイル削除だけを背景化し、完了後に失敗表示と一覧更新を UI へ戻す形へ寄せた
- Bookmark ラベル再生時の FPS 取得用 `MovieInfo` 生成を背景化し、再生位置計算中も UI スレッドを空ける形へ寄せた
- Bookmark reload の dirty 解除を開始時ではなく成功反映後へ移し、背景読込中の追加/削除を取りこぼさないようにした
- watch query-only 局所更新が full reload へ戻る理由を `changed_path_fallback` で残し、局所更新が効かない条件を観測して次の縮小対象を選べるようにした
- watch キュー圧縮の trigger / path 因果ログを追加し、圧縮で見えにくくなる発火理由を `debug-runtime.log` で追えるようにした
- WebView Player 停止・切替時に `user-priority` 解放待ちを残さないよう、WebView surface リセットで pending 解放を畳む方針を反映した
- サムネ進捗の初期 snapshot 全走査を背景化し、下部 `ThumbnailProgress` 初期化が first-page と入力を押しのけないようにした
- 検索確定後の履歴保存・履歴再読込を背景化し、検索完了直後に残っていた同期 DB I/O を UI 導線から外した
- 全体計画の再構築に合わせ、実行レーンを `UIスレッド簡素化`、`diff-first UI`、`watcher/poll 境界安定化`、`起動 warm path`、`visible-first / Player`、`skin 別レーン` の順へ整理した
- `Watcher.cs` の薄化は独立目標から外し、UI thread / queue / shutdown の詰まりを減らす時だけ進める方針へ変更した
- `fire-and-forget` の増加ではなく、bounded drain とログを持つ queue / persister / scheduler へ寄せることを固定した
- 文書の主軸を `rescue` や個別機能の列挙ではなく、`Watcher / UI差分反映` 主導の実行レーンへ組み替えた
- この文書の役割を「本線で今進める実行レーンと完了条件の正本」に絞り、全体プランとの役割重複を減らした
- `rescue / repair` は新規主戦場ではなく、通常動画テンポを壊さないための維持レーンへ再定義した
- `SearchService` の `kana / roma / tag split` は `MovieRecords` 単位の遅延キャッシュへ寄せ、検索確定時の全件再計算を減らした
- `SearchService` の通常検索は、term 解釈を先にコンパイルして各行では比較だけを行う形へ寄せた
- `SearchService` の通常検索マッチングは LINQ の `Any/All` 連鎖を手書きループへ寄せ、比較時の delegate / allocation を減らした
- `{dup}` と exact tag / notag も LINQ 連鎖を縮小し、特殊検索での列挙回数と allocation を減らした
- 起動 deferred services の `CreateWatcher()` は `ApplicationIdle` へ 1 拍後ろ倒しし、first-page 直後の UI tick を軽くした
- 起動 watcher 作成はさらに light services から heavy services 開始時へ移し、first-page 直後は bookmark reload / prewarm だけを先に流して、watch table 読込と監視配備をもう一段後ろへ送った
- 起動サムネ成功インデックス prewarm も背景 task へ逃がし、first-page 直後の UI tick では同期ファイル走査を始めない形へ寄せた
- Bookmark 下部タブの再読込は、`bookmark` DB read と `MovieRecords` 生成を background 化し、UI は `ObservableCollection` 反映だけへ寄せた
- 起動時 auto-open の `system` 先読みをコンストラクタ同期読込から外し、cold start 既定値だけ先に入れて `ContentRendered -> TrySwitchMainDb(...)` へ寄せた
- UI を含む高速化を、個別最適ではなく「全面再評価中心」から「差分反映中心」へ切り替える全体方針として整理
- `FilterAndSort`、watch 終端 reload、画像 I/O、skin 切り替え、起動導線を 1 本の計画で接続
- WhiteBrowser DB (`*.wb`) を変更せず、sidecar / cache / coordinator でテンポを上げる前提を明文化
- watch query-only reload に `changed paths` を通し、`FilteredMovieRecs` の局所再評価で全件 filter を避ける初手を追記
- watch change set に `ChangeKind` を追加し、`empty search + view repair/source insert` では per-path filter を省く現在地を追記
- `DirtyFields` を追加し、rename 系では「検索再判定は必要でも current sort に無関係なら既存順を再利用する」現在地を追記
- `WatchMainDbMovieSnapshot(file_date / movie_size)` と `WatchMovieObservedState` を追加し、Everything 起点の watch existing movie でも cheap な file 属性差分を局所更新へ流せるようにした
- watch existing movie で query-only incremental watch 中かつ `file_date / movie_size` 差分または length 未確定の時だけ metadata probe を許し、`ObservedState.MovieLength` を局所更新へ流せるようにした
- `{dup}` 検索中に `Hash` を含む changed movie が来た時は changed-path 局所更新を降ろし、full in-memory filter へ戻して重複グループの出入りを取りこぼさないようにした
- さらに通常検索では、dirty fields が検索列に無関係な時は現在の一致状態を再利用し、changed-path 局所更新で per-path `FilterMovies(...)` まで省くようにした
- さらに空検索では changed movie の種別に関係なく一致判定を省き、watch query-only で per-path `FilterMovies(...)` を完全に避けるようにした
- さらに `!tag` / `!notag` のようなタグ専用検索では、既存一致行に限って現在の一致状態を再利用し、rename 系でも per-path `FilterMovies(...)` を省けるようにした
- さらに非空検索でも search 非依存 dirty の既存行は、現在一致だけでなく現在不一致の状態も再利用し、metadata 更新での per-path `FilterMovies(...)` をもう一段減らした
- さらに sort 再適用も「今の filtered 結果に残る changed movie」だけで判断するようにし、見えていない変更や検索から外れた行では `SortMovies(...)` まで回さないようにした
- watch の Everything 増分 cursor が無い pass では、既存DB metadata refresh を止める安全弁を追加し、DB切替直後や cursor 不整合時の広域再観測で `FileDate` dirty と `MovieInfo` probe が大量発火しないようにした
- あわせて `load/persist last_sync` と `incremental cursor unavailable` のログに `db / folder / sub / attr` を出し、`debug-runtime.log` だけで cursor 不整合を追えるようにした
- さらに `Auto` でも Everything 増分 cursor を読むようにし、cursor なし周回では既存DB metadata refresh を止めて、広域候補収集がそのまま `FileDate` dirty 大量発火へ繋がらないようにした
- 再読込ボタンや大量変更で最終的に full reload へ戻る周回では、途中の `repair view by existing-db-movie` を止め、最終 `full reload` に一本化してログ氾濫と無駄な局所反映を避けるようにした
- 再読込ボタンは `full filter-sort` と `Manual scan` を並走させず、watch 抑止下で直列化して `FilterAndSort(true) + Manual scan + EverythingPoll` の三重化を避けるようにした
- さらに `manual-reload` 抑止解除直後の catch-up `Watch` は積まず、再読込直後に `watch_zero_diff reconcile` が全量再走査を重ねる二度踏みを止めるようにした
- さらに `manual-reload` 抑止中は `missing-thumb rescue` も同じ周回では走らせず、一覧更新直後に欠損救済キューが雪崩れて UI を止める経路を避けるようにした
- さらに `Header.ReloadButton` 由来の `Manual scan` は再読込完了後に `ApplicationIdle + 250ms` だけ遅延して投入し、一覧更新の完了を先に返すようにした
- さらにプレーヤータブ右側一覧は `Diff/Move` を許可し、`Reset` と追加 `Refresh()` を避けて再読込直後の画像再評価を減らすようにした
- さらに `Header.ReloadButton:deferred` の `Manual scan` 実行中も `missing-thumb rescue` を止め、遅延scan完了直後に救済が雪崩れないようにした
- さらに `manual-reload` 開始時に watch scan scope を進め、走行中の `Auto / Watch` scan を stale 扱いで早めに畳むようにした
- さらにプレーヤータブ右側一覧の画像バインドは、active tab 判定だけでなく viewport の可視・近傍キーにも通し、再読込直後の off-screen `image cache hit` 雪崩を減らす方向へ寄せた
- Player 右レール / visible-first 画像供給では、preferred 対象キーが空と確定した場合は off-screen 画像更新を通さない。未初期化状態や `UpperTabVisibleRange.Empty` の未計測状態は互換として従来どおり許可し、初期化前の表示欠落を避ける。
- Player 右レール / visible-first 画像供給では、preferred key 更新後の反映を full reload や追加 `Refresh()` で起こさない。可視範囲 snapshot の軽い revision 更新、または Binding 用 trigger のみで、既に生成済みの画像 Binding を再評価させる。
- 下部 `ThumbnailProgress` タブが非表示の時は snapshot refresh を即時 UI 更新せず dirty 記録だけへ寄せ、サムネ成功直後の hidden progress 更新が `activity=None` を増やす経路を細くし始めた
- 下部 `ThumbnailProgress` の初期 snapshot 全走査は UI 初期表示から外し、背景作成後に UI へ反映する形へ寄せた
- `SearchSidecar` は本線リポから一旦外し、別リポで継続検証する方針へ切り替えた
- 本線の検索 hot path は、sidecar を使わず既存 `SearchService` 正本のまま `MovieRecords` 単位 cache で軽量化する方針へ寄せた
- 検索窓のインクリメント検索は、常時即時実行ではなく `0.5s debounce` で通常時だけ戻し、起動時部分ロード・IME変換中・途中構文では Enter 確定へ寄せる
- さらに検索確定中は `user priority` スコープで `Auto / Watch` の再走査、`watch_zero_diff reconcile`、`missing-thumb rescue` を defer し、検索完了を背後処理より先に通す
- さらに検索確定時のサムネ常駐再起動は、検索結果反映と先頭選択の後へ置き、検索結果表示を先に返す
- 検索欄を空へ戻す解除導線も同じ検索正本へ寄せ、旧来の `RestartThumbnailTask()` 先行を戻さない
- さらに検索詰まりの切り分け用に、`FilterAndSortAsync(...)` の観測点を `db-reload / source-apply / filter-movies / sort-movies / replace-filtered` まで分解し、実機ログだけで hot path を断定できるようにした
- さらに通常検索の比較は、ASCII 系検索語だけ `OrdinalIgnoreCase` の軽い比較へ寄せ、日本語など非 ASCII を含む語は従来どおり `CurrentCultureIgnoreCase` を維持して `filter-movies` の hot path を軽くし始めた
- さらに ASCII 検索では `Movie_Name / Movie_Path / Tags / Comment1-3 / Roma` だけを見る軽量投影 cache を使い、`kana / katakana` 派生列の全件生成を避けて `filter-movies` の詰まりを減らし始めた
- 実機確認では `ggggg` のような ASCII 検索で `filter-movies` が完了しない事象を再現し、軽量投影 cache 追加後は検索完了まで進むことを確認した。ASCII 検索 hot path の主因は、比較順より `kana / katakana` 派生列の全件生成だったと整理する
- さらに textbox 入力の重さは `SearchBox_TextChanged(...)` ごとの `RestartThumbnailTask()` 連打が主因だったため、通常入力中はサムネ常駐を再起動せず、実検索の瞬間だけ再起動する形へ寄せた
- さらに検索確定後の履歴保存・履歴再読込は background task へ逃がし、DB が同じ時だけ UI へ履歴候補を反映する形へ寄せた
- `UiHang` オーバーレイの終了時残留は、常時の無通信 timer を主解にせず、owner 付与、caller 側 hide 保証、overlay thread shutdown 強制線の順で解く方針を固定した。無通信 timer は shutdown 専用 safety fuse としてのみ扱う
- `NativeOverlayHost` の overlay thread は `try/finally` で終了出口を固定し、起動直後の例外や stop 競合でも `DestroyOverlayOnCurrentThread()` を必ず通す
- Stop timeout 後に overlay support が再 Start された場合でも、古い overlay thread の `finally` は一致する thread / dispatcher だけをクリアし、新しい管理状態を壊さない
- overlay thread 内例外は `catch (Exception ex)` で記録して終了処理へ流し、補助表示の失敗がアプリ本体の終了要因にならないようにする
- overlay dispatcher action 内例外は `Dispatcher.UnhandledException` で記録して `Handled=true` にし、描画・配置更新失敗を補助表示内へ閉じる
- Stop timeout 後に古い overlay thread が alive の間は Start をスキップし、古い thread が自然終了してから次の Start に任せる
- `Watcher.cs` は入口と中盤の `watch table load failure`、`visible gate`、`scan strategy detail`、`full reconcile` 入口判定を helper / policy 側へ寄せ続け、`CheckFolderAsync(...)` を orchestration 専念へさらに寄せた
- `Everything poll` は watch folder snapshot、eligible 判定再利用、重複 path 除去、low-update 時の間隔延長まで入り、通常周回の CPU / wakeup コストを下げ始めた
- `RunEverythingWatchPollLoopAsync(...)` は初回待機後に UI コンテキストへ戻らない形へ寄せ、周期判定と queue 投入を背後側で進めるようにした
- poll loop が背後側で読む Player 再生中状態と起動 partial 状態は `Volatile.Read/Write` 経由へ寄せ、UI から更新される軽量状態を安全に共有する形へ固めた
- `DBInfo.DBFullPath` も `Volatile.Read/Write` 経由へ寄せ、Everything poll が UI 外で現在DBパスを参照する前提を明示した
- Everything poll の watch folder snapshot / eligible snapshot は cache 参照と invalidation を同じ lock へ寄せつつ、watch table 読み取りと eligible 判定は lock 外へ逃がし、poll loop 背後化後の共有状態境界を固めた
- `QueueCheckFolderAsync(...)` は enqueue 後の check-folder queue runner を ThreadPool 起動かつ 1 本共有へ寄せ、UI 操作から入った watch / manual scan でも呼び出し元 UI スレッドに同期前半を残しにくくした
- `CreateWatcher()` は起動後の watcher 作成計画を背景側で組み、watch table 読み込み / Everything availability 判定 / skip 判定を UI から外し、UI には DB 切替ガードと revision 確認後の `FileSystemWatcher` 登録だけを残した
- watcher 作成計画の `Everything-only` skip は登録直前に availability を再確認し、計画作成後に Everything が落ちた時は `FileSystemWatcher` 登録へ戻すようにした
- watcher 登録フェーズでは UI 上の `Path.Exists(...)` 再実行を避け、背景計画で確認済みの対象だけを登録する形へ寄せた
- watcher 作成 task は active count と最新 task 状態を shutdown handoff ログへ残し、終了時に未完了の背景作成があるかを追えるようにした
- `CheckFolderAsync(...)` の watch table は共有 `watchData` を更新せず、走査ごとのローカル snapshot で回す形へ寄せ、背景走査と UI 表示用データの境界を分けた
- ファイルコピー / リネーム時の watcher 一時停止復旧は、DB切替や終了で watcher が破棄済みでも例外をログへ閉じ、UI 操作完了を壊さないようにした
- 起動直後の EverythingLite root prewarm は非同期 watcher apply 後の共有 `watchData` に依存せず、背景側で watch table snapshot を読んで空振りを避ける形へ寄せた
- `WatcherEventQueue` の runner 起動も ThreadPool へ寄せ、FileSystemWatcher event handler から初回処理の同期前段をさらに外した
- 外部 skin API の非 UI スレッド経由の同期 UI 状態読み取り / UI 操作は `DispatcherPriority.Background` へ下げ、戻り値 API の互換を維持しつつ入力・描画を押しのけにくくした
- 外部 skin API は UI 状態を `WhiteBrowserSkinApiUiSnapshot` として 1 回だけ固定し、`update / getInfo / getInfos / getFindInfo` の DTO 生成で DB パス、thumb folder、選択状態、検索条件、基準 sort を個別再読取しない入口へ寄せた
- `update / getInfo / getInfos / getFindInfo` は DTO 生成前に必要値だけの `MovieRecords` クローンを作り、途中で UI 側モデルが変わっても 1 応答内の値が揺れにくい形へ進めた
- `focusThum / selectThum / addTag / removeTag / flipTag` は対象解決では UI 実体 `MovieRecords` を維持し、操作後レスポンスだけを操作後 snapshot と値クローンへ寄せた
- 外部 skin API の thumbnail size 解決は、WB メタが十分な managed sheet ではメタ値を優先し、不要な同期画像デコードを避ける入口へ寄せた
- 外部 skin API の thumbnail 解決は `FullSync / CacheOnly` の明示モードを持ち、`getInfo` と ID/recordKey 指定 `getInfos` は詳細精度維持、範囲 bulk `update / getInfos` は cache miss 時に既定寸法へ落として同期画像デコードを避ける形へ寄せた
- 外部 skin API の thumbnail 更新 callback は、UI スレッドに残す処理を最小 context 取得へ寄せ、DTO 組み立ては UI 外へ逃がしつつ、連続通知の順序逆転を避ける直列 callback queue と fault log を持たせた
- 外部 skin API の `CacheOnly` stale は、ファイルスタンプ確認で古いサイズ cache を捨て、未キャッシュ時は同期画像 decode へ戻らず WB メタまたは既定寸法へ落とす形へ統一した
- 外部 skin API の thumbnail サイズ cache は `FullSync / CacheOnly` を分け、`CacheOnly` の WB メタ値が後続 `FullSync` の画像実寸解決を汚染しないようにした。callback も stale host/tab は DTO 構築前に落として、古い通知の重い組み立てを避ける
- watcher / poll 境界は、Everything 対象 folder が無い周回や queue probe を見送った周回で短周期 wake-up / queue DB 参照へ戻りにくいよう、eligible 状態と前回遅延を使う待機方針へ寄せた
- `EverythingWatchPollPolicy`、`UiHang` overlay lifecycle、`WatchScanCoordinator`、`WhiteBrowserSkin` API/thumbnail 契約、Player resize hook、startup warm path の source test を追加・拡張し、今回の境界変更をテストで固定した
- `Watcher.cs` はさらに、`context 初期化`、`background scan`、`scan pipeline`、`movie loop`、`pending flush`、`folder completion`、`run finish`、`folder failure recovery result` を helper / runtime 側へ寄せ、入口・中盤・終端を段単位で薄くした
- `WatchLoopDecision` を `movie loop` と `pending flush` の共通戻り値へ揃え、`return / break / continue` の flow を同じ読み筋で追える形へ寄せた
- `watch folder` 解決、`scan 準備`、`movie loop preparation`、`loop decision await/apply`、`folder phase result`、`run finish` 呼び出しも helper 化し、`CheckFolderAsync(...)` は段ごとの orchestration を読む形へさらに近づいた
- `scan strategy 通知` と `scan mode 診断` は runtime 側で束ね、`Watcher.cs` 側は orchestration と通知入口に専念する形へ整理した
- `WatcherEventQueue` は処理 task を 1 本共有し、enqueue ごとに queue runner を増やさない形へ寄せて watch burst 時の先頭詰まり増幅を抑えた
- `Created` の ready 待機は queue runner から分離して直列専用パイプラインへ逃がし、`Renamed` を `Created` 待ちで止めない形へ整合を補強した
- 同一 path の `Created` 連続投入は ready 待ち pipeline で圧縮し、先行が scan へ合流できなかった場合だけ重複通知分を短く再確認する。
- 旧パス未登録の `Renamed` は watch scan へ再合流させ、`Created -> Renamed` 連鎖で rename だけ先行した場合でも最終整合を回収する形へ寄せた
- watch query-only full 戻り理由ログ、watch キュー圧縮の因果ログ、WebView 停止時の `user-priority` pending 解放、サムネ進捗初期全走査の背景化は完了済みとして扱う

## 1. 目的

- ユーザーが最初に触る一覧、検索、ページ移動、watch 反映、skin 切り替えの体感テンポを、局所改善ではなく構造変更で底上げする。
- 検索、並び替え、ページ移動、タブ切り替えなどの明示的なユーザー要求は、watch / rescue / thumbnail / poll などの背後処理より最優先で完了させる。
- 高速化の主戦場を「重い処理を少し速くする」から、「重い処理をそもそも起こさない」へ移す。
- 通常動画の初動を守りながら、rescue / queue / watcher / skin の既存本線と矛盾しない着手順を固定する。

## 2. いまの見立て

本丸は仮想化不足ではない。主因は、少数変更でも全面再評価へ戻る構造が複数箇所に残っていること。

### 2.1 一覧再評価

- `Views/Main/MainWindow.xaml.cs:1560` の `FilterAndSortAsync(...)` は、DB再読込、source 差し替え、全件 filter/sort、`Refresh()` までを 1 本で持つ。
- `Infrastructure/SearchExecutionController.cs` と `Views/Main/MainWindow.Search.cs` では、通常の検索確定を `query only recompute` 側へ寄せ始めている。`RefreshMovieViewAfterRenameAsync(...)` も、rename 後の一覧再計算をメモリ上 read model だけで回す初手まで入っている。
- `Infrastructure/SearchService.cs` は、検索仕様の正本を維持したまま `MovieRecords` 単位の遅延 cache を使い、`kana / katakana / roma / normalized tags` を毎回再生成しない形へ寄せた。
- 一方で、起動直後の部分ロード中は full reload を維持する意味論が残っており、`query only recompute` と `full snapshot reload` の境界はまだ育成中である。
- `Watcher/MainWindow.Watcher.cs`、`Watcher/MainWindow.WatcherUiBridge.cs`、`Watcher/MainWindow.WatcherRenameBridge.cs`、`Watcher/MainWindow.WatchScanCoordinator.cs` では、watch 後の最終 reload と rename 後追従を軽量化し始めているが、大量変更時や起動時部分ロード中は full reload へ戻す境界をまだ整理中である。
- 直近では、watch 側で `changed paths + ChangeKind` を集約し、`Views/Main/MainWindow.xaml.cs` の in-memory refresh へ渡して「現在の `FilteredMovieRecs` から changed paths だけ抜き差しして再検索する」経路を追加した。これで検索結果が総件数より十分小さい時は、watch query-only でも全件 filter を避けられる。
- さらに `empty search` かつ `source insert / view repair / displayed refresh` の時は、per-path `FilterMovies(...)` すら省いて直接復帰できる現在地まで入った。
- rename 系では `MovieName / MoviePath / Kana` の dirty fields を明示し、current sort がそれらに依存しない時は full sort を避けて既存順を再利用する現在地まで入った。
- watch existing movie でも、Everything 起点の changed path に限っては `file_date / movie_size` の cheap な観測値を `ObservedState` として source `MovieRecords` へ当て、DB 再読込なしで局所更新へ載せる現在地まで入った。
- さらに query-only incremental watch 中で cheap 差分または DB length 未確定の時だけ metadata probe を許し、watch existing movie の `MovieLength` 変更も `ObservedState` 経由で局所更新へ載せる現在地まで入った。
- ただし `{dup}` 検索だけは changed path 外の既存行も結果へ出入りするため、`Hash` 変化時は changed-path 局所更新を使わず full in-memory filter へ戻す安全弁を入れた。
- その一方で通常検索では、`MovieSize / FileDate / MovieLength` など検索非依存 dirty の時は changed path ごとの `FilterMovies(...)` も省き、現在の一致状態をそのまま再利用する現在地まで入った。
- つまり「変更件数は少ないのに、結果として一覧全体を考え直す」経路が残っている。

### 2.2 画像表示

- `Infrastructure/Converter/NoLockImageConverter.cs:51` 以降は改善済みだが、miss 時は `FileInfo` と decode を踏む。
- `Infrastructure/Converter/NoLockImageConverter.cs:292` の metadata miss は往復スクロールやページ移動でまだ効く。
- 直近では、UI スレッド上の画像読込失敗時だけ再試行 `Thread.Sleep(20)` を行わず、1回失敗で次回描画へ譲るようにして visible-first の詰まりを減らした。非 UI スレッドでは従来の短い再試行を維持する。
- visible-first は進んだが、「今見えている範囲だけを優先する」思想が一覧全体の更新経路までは貫通していない。

### 2.3 skin 切り替え

- `WhiteBrowserSkin/WhiteBrowserSkinOrchestrator.cs:97` の apply は簡潔になったが、skin 解決、初期タブ解決、persist、host refresh が近接している。
- `WhiteBrowserSkin/WhiteBrowserSkinOrchestrator.cs:232` では definition 解決時に catalog load を伴う。
- catalog cache と refresh scheduler は入ったが、skin 切り替え全体を「表示切替」と「保存・整合」に完全分離し切ったとはまだ言えない。

### 2.4 起動と常駐開始

- 起動段階ロード化で first-page 化は進んだが、起動後の watch / bookmark / queue / skin 関連の warm path はまだ分散している。
- 直近では、起動時 auto-open の `system` 先読みをコンストラクタから外し、最初の表示前は cold start 既定値だけを使って `ContentRendered` 後の DB 切替へ寄せた。
- さらに Bookmark 下部タブの再読込も、`bookmark` DB read と item 生成を background 化し、UI スレッドには結果反映だけを残し始めた。
- さらに Bookmark 追加時のメタ取得、bookmark フォルダ作成、DB 登録も UI クリック処理から外し、サムネ生成成功後に背景 DB 登録してから一覧 reload する形へ寄せた。
- さらに Bookmark 削除時の DB 書き込みも背景化し、削除クリックでは対象 ID と DB パスの snapshot だけを持つ形へ寄せた。
- さらに Player 再生開始後の score / view_count / last_date と Bookmark 再生回数更新も背景保存へ寄せ、再生クリック後の DB 待ちを減らした。
- さらにスコア増減メニューも、UI 側では `MovieRecords.Score` の表示値更新だけを先に行い、DB 更新は背景 task で実行してクリック導線を塞がないようにした。
- さらにタグメニュー、下部タグ編集タブ、タグチップ削除も、UI 側では `MovieRecords.Tag/Tags` の表示更新だけを先に行い、DB 更新は共通背景 helper へ逃がす形へ寄せた。
- さらにファイル移動メニューも、物理移動成功後の `MovieRecords.Movie_Path` 表示更新を先に行い、`movie_path` DB 更新は背景 helper へ逃がす形へ寄せた。
- さらにファイルコピーは、コピー要求を snapshot して `File.Copy(...)` を背景 task へ逃がし、watcher 抑止の復旧を `finally` で保証する形へ寄せた。
- さらにファイルリネームも watcher 抑止と復旧を共通 helper で扱い、`RenameThumb(...)` 呼び出し中の例外で監視停止が残らないようにした。
- さらにサムネイルのみ削除は、選択レコードとサムネパスを snapshot し、ファイル削除は背景 task、完了後の失敗表示と `FilterAndSort(...)` だけ UI へ戻す形へ寄せた。
- さらに Bookmark ラベル再生の FPS 取得用 `MovieInfo` 生成も背景化し、外部プレイヤー起動前の動画メタ取得で UI を塞がないようにした。
- さらにサムネイルシート再生位置解析も `Task.Run` へ逃がし、クリック位置からの `ThumbInfo` 読み取りを UI スレッドに残さないようにした。
- Player 音量設定の実 `Save()` は debounce 後に直列背景保存へ寄せ、スライダー操作中の設定ファイル I/O を UI から外した。
- MainDB 切替時の `LastDoc` / ダイアログフォルダ保存、終了時の最近使ったファイル保存、Player fullscreen debug、詳細サムネ表示モード、Log タブ debug カテゴリ切替の一時設定保存も、共通の背景保存キューへ寄せた。終了時は短時間 drain して保存取りこぼしを防ぐ。
- DB 切替後の旧 QueueDB pending 掃除は背景 task へ逃がし、切替成功後の UI 待ちに queue cleanup I/O を残さないようにした。
- さらに Bookmark reload の dirty 解除を成功反映後へ移し、revision 不一致や DB 切替後着では dirty を消さずに捨てるようにした。
- さらに起動 deferred services の `CreateWatcher()` も `ApplicationIdle` へ後ろ倒しし、first-page 直後の UI tick に watch table 読込と watcher 配備を詰め込まないようにした。
- 直近では watcher 作成を light services から heavy services 開始時へ移し、first-page 直後の UI tick では bookmark reload と軽い prewarm を優先する段差へ寄せた。
- サムネ成功インデックス prewarm も背景 task へ移し、first-page 直後の UI tick ではファイル索引構築を始めないようにした。
- warm start をさらに詰めるには、起動直後に必要な read model と、後で良い常駐処理をより明確に分ける必要がある。

### 2.5 `UiHang` オーバーレイ終了残留

- `Views/Main/UiHangNotificationCoordinator.cs` の `Stop()` は `_overlayHost.Hide(); _overlayHost.Stop();` を呼ぶが、hide と shutdown の実体は別スレッド dispatcher への依頼中心である。
- `Views/Main/NativeOverlayHost.cs` は native overlay を owner なしの `CreateWindowExW(...)` で作っており、本体ウインドウ終了へ OS の owner-chain を使って追従していない。
- `Views/Main/NativeOverlayHost.cs` の `Stop()` は join timeout 後に `overlay thread still alive after shutdown request` で諦めうるため、終了競合時に HWND が取り残される余地がある。
- したがって主因は「表示中の overlay 自体」より、「overlay の寿命管理が owner なし + overlay thread 正常応答前提」な点にある。
- `無通信timer` は見た目を減らす safety fuse にはなるが、overlay thread 側が詰まると効かず、UI 本当に詰まり中でも誤って hide しうるため主解にはしない。

## 3. 抜本方針

結論は 1 つ。

**一覧 UI を「全面再評価UI」から「差分反映UI」へ変える。**

この方針を成立させるため、この文書では以下の実行レーンで進める。

1. Lane 0: 計測固定と作業差分保全
2. Lane 1: UIスレッド簡素化と入力優先
3. Lane 2: watch change set と diff-first UI の一本化
4. Lane 3: watcher / poll / shutdown 境界の安定化
5. Lane 3.5: `UiHang` オーバーレイ寿命管理の是正
6. Lane 4: 起動 warm path の再短縮
7. Lane 5: visible-first / Player / 画像供給
8. Lane 6: `skin` 切り替えの表示・保存完全分離
9. Lane 7: rescue / repair 維持レーン

## 4. 非機能の固定ルール

1. WhiteBrowser DB (`*.wb`) のスキーマは変更しない。
2. sidecar は補助であり正本にしない。壊れても fallback で戻れることを前提にする。
3. UI スレッドへ重い処理を戻さない。
  ただしユーザー要求を先に通すための背後処理抑止・延期は積極的に採る。
4. 高速化のために観測性を削らない。
5. rescue / repair / queue の既定動作を重くしない。
6. 検索の正本は既存 `SearchService` に置き、本線ではここを基準に保守する。
7. `UiHang` オーバーレイ残留は `無通信timer` だけで隠さない。owner / lifecycle / shutdown guarantee を正してから、最後に shutdown 専用 fuse を足す。

## 5. 実行レーン

## Lane 0: 計測固定と作業差分保全

目的:
- 改善前後を感覚ではなく数値で比較できるようにする。
- 未コミットの別作業を混ぜず、計画・実装・検証の単位を小さく保つ。

実施内容:
- `filter start/end`、watch reload、page append、thumbnail decode、skin refresh の trace id を揃える。
- 指標を `debug-runtime.log` で横断して読めるようにする。
- 2026-05-03: watch 終端 reload の判断理由を `plan_reason` として `debug-runtime.log` に残す。`watch-full-fallback` が残る経路を次の差分化候補として扱う。
- 2026-05-03: deferred watch reload は schedule と apply の両方で同じ `plan_reason` を残す。圧縮後の reload でも query-only 由来か full fallback 由来かを追えるようにする。
- 2026-05-03: deferred watch reload が適用されなかった理由を `skip_reason` として `debug-runtime.log` に残す。`revision-stale` / `db-changed` / `ui-suppressed` を分けて shutdown と DB 切替の後着を追えるようにする。
- 2026-05-03: deferred watch reload の consume 失敗も `skip_reason=not-pending / revision-stale` で残し、二重消費と古い要求を分けて追えるようにする。
- 2026-05-03: watch 終端 reload ログへ `can_query_only` を追加する。`watch-full-fallback` が query-only 不可由来かを実機ログから絞り、次の差分化候補を選ぶ。
- 2026-05-25: watch bulk 復帰の full fallback 理由を `dirty-fields-unsafe:*` / `change-kind-unsafe:*` まで細分化した。no-op だけなら復帰しないが、no-op が安全な既存行差分に混ざるだけなら query-only 復帰を維持する。
- 着手前に `git status --short` で対象外差分を確認する。
- `layout*.xml` や実機操作で動く差分は、目的が一致する時だけ扱う。
- 最低限の計測点を固定する。
  - 起動: `ContentRendered -> first-page shown`
  - 一覧: `search input -> filtered apply end`
  - watch: `event accepted -> ui diff applied`
  - skin: `apply requested -> host presented`
  - 画像: `viewport request -> image ready`
- 2026-05-03: 実 WebView2 を使う `MainWindowWebViewSkinIntegrationTests` は `WebView2Real` / `MainWindowWebViewSkin` category で識別する。全件一括では testhost shutdown 側のクラッシュが出るため、リリース前は category と `Name` filter を併用して小分け検証する。
- 2026-05-03: 小分け検証の入口として `Tests\IndigoMovieManager.Tests\Run-WebView2SkinIntegrationChunks.ps1` を追加。既定順は `HostBasics` / `TutorialCallback` / `DefaultList` / `SimpleGrid` / `TagInputSmoke` / `TreeSmoke` / `BuildOutputSkins`。`TagInputRelation` 全体や tree 系全体の broad filter は testhost shutdown 側のクラッシュ再現域なので、まず smoke chunk から通す。
- 2026-05-03 実測: `HostBasics` 23件、`TutorialCallback` 9件、`DefaultList` 4件、`SimpleGrid` 8件、`TagInputSmoke` 6件、`TreeSmoke` 3件、`BuildOutputSkins` 109件は、ビルド込みの既定順連続実行で成功。
- 2026-05-03 追加確認: poll / queue 境界変更後も `FullyQualifiedName~Watch` は 322件成功、`Run-WebView2SkinIntegrationChunks.ps1 -NoBuild -Chunk HostBasics` は 23件成功。
- chunk 名の確認は `pwsh -NoProfile -ExecutionPolicy Bypass -File Tests\IndigoMovieManager.Tests\Run-WebView2SkinIntegrationChunks.ps1 -ListChunks` を使う。

完了条件:
- どこが遅いかを「起動」「一覧」「watch」「skin」「画像」で分けて説明できる。
- コミットが 1 目的で、対象外差分を含まない。

## Lane 1: UIスレッド簡素化と入力優先

目的:
- watch / DB / thumbnail / skin / poll の仕事が、検索入力、ページ移動、Player 操作、描画を塞がないようにする。
- ユーザーの明示操作を、背後処理より先に通す。

実施内容:
- `Search / page / Player / tab` 操作中は、`Auto / Watch`、`watch_zero_diff reconcile`、rescue、thumbnail progress、poll を必要に応じて defer する。
- UI スレッド上の DB read/write、catalog scan、file metadata、decode、collection 全差し替えを見つけたら、まず snapshot / queue / background read へ分離する。
- preferred サムネ生成成功後も、対象 `MovieRecords` へ直接 path 反映できた場合は `FilterAndSort(..., true)` へ戻さず、forced rebind と visible refresh を優先する。
- manual rescue 即時反映も同じ基準に寄せ、直接反映できた成功では後段 full reload を予約しない。
- rescued sync の定期反映では FailureDb / progress 更新を維持しつつ、選択中レコードへ当たった時だけ `Refresh()` を許可する。
- Rescue 履歴は選択変更時に FailureDb を UI スレッドで読まず、背景読込と revision guard 付き反映へ寄せる。
- Log タブ preview は UI 側で表示対象パスと active / force 判定だけを取り、ファイル存在確認、mtime、末尾 preview 読みは背景 helper へ逃がし、後着 request id guard で古い結果を捨てる。
- `ThumbnailProgress` の enqueue / 初期作成数反映は `ThumbnailProgressRuntime.CurrentVersion` の差分 guard を通し、runtime 状態が変わらない時は snapshot 予約を増やさない。
- `fire-and-forget` で逃がすだけにせず、bounded drain、timeout ログ、fault ログを持つ scheduler / persister / queue へ寄せる。
- `Watcher.cs` の薄化は、UI thread 滞在、queue 境界、shutdown 境界、観測性の改善につながる場合だけ進める。

完了条件:
- 明示的なユーザー操作中に、背後処理が full reload / full scan / heavy DB I/O を重ねない。
- UI スレッドに残る仕事を、短い snapshot と ObservableCollection 反映へ説明できる。
- queue / scheduler / persister の終了時動作を `input stop -> complete -> bounded drain -> timeout log` で説明できる。

## Lane 2: watch change set と diff-first UI の一本化

目的:
- 一覧更新時の仕事量を「総件数依存」から「変更件数依存」へ寄せる。
- watch、rename、検索変更で同じ全面再評価経路へ戻る構造を崩す。

実施内容:
- `MainWindow` 直下にある検索条件、ソート、上側タブ状態、ページ状態を `QueryState` として 1 か所へ寄せる。
- `movieData -> MovieRecords[] -> FilteredMovieRecs` の都度組み立てをやめ、read model 更新と view query 適用を分離する。
- `isGetNew=true` の full reload と、検索語変更・ソート変更・watch 差分反映を同じ入口で扱わない。
- まず以下の 3 種に分ける。
  - full snapshot reload
  - query only recompute
  - item diff apply
- `MainVM.ReplaceFilteredMovieRecs(...)` を中核にしつつ、一覧反映の前段に `FilteredMovieDiffCoordinator` 相当を置く。
- watch / rescue / manual reload の反映を「追加」「削除」「更新」「順位変更」に分ける。
- `FilterAndSort(..., true)` を watch の既定終端から外し、小規模変更は差分 apply を既定にする。
- watch query-only では `MovieRecs` 全件を毎回 `FilterMovies(...)` に通さず、`changed paths` だけを再評価して `ReplaceFilteredMovieRecs(...)` へ渡す経路を育てる。
- changed path 局所更新の lookup も、全件辞書化ではなく変更対象 path だけを保持し、少数変更時の allocation を変更件数寄りへ寄せる。
- bulk 降格後の最終判定では、既存行の安全な view / dirty 差分だけなら query-only へ戻す。圧縮過程で混ざる no-op 札は有効差分から外し、no-op だけの要求は復帰扱いにしない。
- 検索等のユーザー要求中は、watch full / bulk reload、zero-diff reconcile、rescue、thumbnail などが完了を妨げないよう後ろへ逃がす。
- 全面再評価が必要な条件だけを明示する。
  - sort key 変更
  - query 条件変更
  - 大量変更しきい値超過
  - DB 切り替え
- 検索高速化の別リポ検証は継続してよいが、本線へ戻す時は既存検索仕様と fallback 条件を先に揃える。

完了条件:
- watch の 1 件追加や rename で `Refresh()` 全面経路を常に踏まない。
- 「小規模差分」と「全面再評価」の境界がコード上で説明できる。

## Lane 3: watcher / poll / shutdown 境界の安定化

目的:
- Watcher と Everything poll を別スレッド化・非同期化しても、終了時や DB 切替時に取りこぼしや二重実行を出さない。

実施内容:
- `FileSystemWatcher` 入力停止、watch event queue complete、created ready pipeline drain、Everything poll 停止を順序付きで扱う。
- Created ready 待機は通常時の retry 契約を保ちつつ、shutdown 開始後は短い分割待機で stale skip し、detached task を残さない。
- check-folder queue runner も同じ shutdown drain deadline 内で待ち、追加の500ms待機を増やさず `check-folder-queue-runner` として timeout / fault を追えるようにする。
- Everything poll loop は UI コンテキストへ戻らない待機を使い、周期判定を UI tick と競合させない。
- low-update 時の poll interval 延長を、初期処理が落ち着いた後だけ有効化する。
- `watch folder snapshot` と eligible 判定 cache の invalidation 条件を、DB 切替 / watch folder 編集 / settings 変更へ限定する。
- Everything poll の DB path 存在確認は policy 注入 delegate に統一し、instance 側で確認済みの DB 存在結果を snapshot 取得へ渡して、通常周回で DB path probe を二重三重に重ねない。
- check-folder queue runner は UI スレッドから直接走らせず、enqueue と処理実行の境界を分ける。runner task は 1 本共有にし、burst 時に待機 task を増やしすぎない。runner fault は `watch-check` へ残し、await している経路には例外をそのまま返す。
- queue の mode 圧縮で trigger / path の因果が消えすぎる箇所は、追加済みログを維持し、必要なら軽量 DTO へ広げる。

完了条件:
- shutdown 中に watcher / poll / created pipeline が新規要求を増やさない。
- low-update 時の poll 延長が、初期同期や大量変更の見逃しを生まない。
- `debug-runtime.log` だけで event accepted / deferred / drained / skipped を追える。

## Lane 4: 起動 warm path の再短縮

目的:
- first-page を最優先し、その後ろへ送れる処理は `ContentRendered` や `ApplicationIdle` 後へ寄せる。
- 起動完了を `first-page shown / input ready / heavy services started` に分け、UI が触れるまでの待ちを縮める。

実施内容:
- 起動時 read model を first-page 用と background append 用に明確分離する。
- `CreateWatcher()`、bookmark reload、tag / queue warm path を UI 入力可能後へ順次開始する。
- `OpenDatafile(...)` 後に必要な同期仕事をさらに削り、「表示」「操作可能」「常駐起動完了」を別イベントとして扱う。
- `ThumbnailProgress` snapshot は `ContentRendered` 直後に直接作らず、first-page 後の startup light services から既存の coalesce 経路へ予約する。
- fallback 起動も `QueueStartupThumbnailProgressSnapshotRefresh()` へ合流し、直接更新を増やさずに進捗表示の置き去りを抑える。
- warm start 用の補助 cache を使う場合も `LocalAppData` 配下に限定し、壊れても DB fallback に戻せる形を守る。
- `Everything poll` は watch folder 一覧の snapshot と eligible 判定再利用を前提にし、DB 切替や監視フォルダ編集時だけ invalidation する。通常周回では毎回 `watch` テーブルと同一 path の eligibility を掘り直さない。

完了条件:
- 起動完了を 1 点ではなく、3 段階のイベントで説明できる。
- 大 DB でも first-page 直後の入力待ちがさらに短くなる。

## Lane 5: visible-first / Player / 画像供給

目的:
- 一覧スクロール、ページ Up/Down、詳細表示で「見える範囲に関係ない I/O」を減らす。
- Player / UpperTabs の表示・再生操作を、一覧や watcher の背後処理から守る。

実施内容:
- `NoLockImageConverter` の metadata cache を viewport 連動で活かし、表示候補の先読みと無効化を分ける。
- visible range 外の decode をより後ろへ倒し、可視範囲だけ即時 decode する。
- Player 右レール / visible-first の preferred 対象キーは、「未初期化 / viewport 未計測」と「空と確定」を分ける。未初期化や `UpperTabVisibleRange.Empty` は互換として従来どおり off-screen 画像更新を許可し、空と確定した後だけ off-screen 更新を止める。
- 通常上側タブの画像 Binding も Player 右レールと同じ preferred key gate / revision trigger を通し、active tab 内の off-screen decode を増やさない。
- preferred key 更新後は、右レール全体の再構築や full reload ではなく、可視範囲 snapshot の軽い revision 更新、または Binding 用 trigger だけで実現済み画像 Binding を再評価する。既に `MovieRecords` へ path 反映できている画像を、一覧の全面再評価で揺らさない。
- preferred / manual rescue の即時成功も、直接反映済みなら viewport 再計測を省き、preferred key revision だけを進める。
- 画像存在確認と file stamp 取得を、converter 個別呼び出しから `ThumbnailStampCache` 相当へ寄せる。
- 詳細パネル、タグ、bookmark などの補助 UI は、表示された時だけ decode / bind を始める。
- Player / UpperTabs の追加・ fullscreen・表示切替は、watch / thumbnail / skin の背後処理と同時に走っても UI スレッドを長く掴まない形へ寄せる。
- WebView Player の動画切り替えでは、ホスト側音量適用中の通知を保存せず、切り替え直後の 100% 既定音量通知でユーザー音量を上書きしない。
- Player 通常再生前の実ファイル存在確認は、UI 側では選択パス snapshot だけにして `Task.Run` helper へ逃がす。存在しない時の早期 return と、その後のサムネイル再生位置解析・外部プレイヤー起動順は維持する。
- Bookmark 追加は、UI 側で選択行・再生位置・DB パスだけ snapshot し、動画メタ取得と DB 登録は背景へ逃がす。DB 切替後着は現在 DB 一致で捨てる。
- Bookmark 削除も、UI 側では対象 ID と DB パスだけ snapshot し、DB 書き込みは背景へ逃がす。
- Player 再生時の統計保存も、UI 側では表示値更新だけ先に行い、DB 書き込みは背景へ逃がす。
- スコア増減メニューも、UI 側では表示値更新だけ先に行い、DB 書き込みは背景へ逃がす。
- タグメニュー、下部タグ編集タブ、タグチップ削除も、UI 側ではタグ表示更新だけ先に行い、DB 書き込みは背景へ逃がす。
- ファイル移動後の `movie_path` 保存も、表示反映後の背景 DB 書き込みへ寄せる。
- ファイルコピーは、UI 側でコピー元/先を snapshot してファイル I/O を背景へ逃がす。
- 親フォルダを開く操作は、UI 側で動画パスと親フォルダだけ snapshot し、存在確認を背景へ逃がしてから Explorer 起動だけ UI へ戻す。
- ファイルリネームは、watcher 抑止と復旧を `try/finally` で固定してから重い後続処理を薄くする。
- サムネイルのみ削除は、UI 側で対象とパスを snapshot してファイル削除を背景へ逃がす。
- Bookmark ラベル再生の FPS 取得も背景化し、再生位置計算中に UI スレッドを掴まないようにする。
- Bookmark reload の dirty 解除は成功反映後に限定し、古い背景読込結果で未反映更新を消さない。

完了条件:
- ページ Up/Down 時の体感引っかかりが、cache miss 頻度とともに下がる。
- off-screen 領域の decode が visible 領域を押しのけない。
- preferred key 更新後の画像再評価が、軽い revision / trigger だけで完結し、`FilterAndSort(..., true)`、full reload、追加 `Refresh()` を既定経路に戻していない。
- Player 操作中に watch / thumbnail / poll が目に見える詰まりを増やさない。

## Lane 3.5: `UiHang` オーバーレイ寿命管理の是正

目的:
- 終了後に overlay が取り残される事象を、見た目のごまかしではなく寿命管理の正攻法で止める。

実施内容:
- `NativeOverlayHost` の native overlay を MainWindow owner 付き popup として生成し、本体終了へ OS レベルで追従させる。
- `StopUiHangNotificationSupport()` からの停止では、overlay thread dispatcher 依頼より前に caller 側から即 hide を保証する。
- overlay thread の join timeout 後は、`InvokeShutdown()` 任せで終わらせず、強制閉鎖線を持つ。
- そのうえで最後の保険として、shutdown 開始後だけ効く stale fuse を検討する。常時の無通信 timer は入れない。

完了条件:
- `MainWindow` 終了後に overlay が残らない。
- overlay 残留対策が、平常時の UI hang 通知誤抑止を生まない。
- `debug-runtime.log` だけで `hide request -> stop requested -> thread destroyed` まで追える。

## Lane 6: `skin` 切り替えの表示・保存完全分離

目的:
- skin 切り替えを UI テンポ視点でさらに細くし、見た目更新と保存の干渉を切る。

実施内容:
- `ApplySkinByName(...)` は「表示切替要求の確定」までに責務を絞り、persist は非同期経路の completion へ完全移譲する。
- current definition 解決、tab state 解決、persist request、host refresh request を trace 単位で分離する。
- catalog load は起動時 snapshot と変更検知に寄せ、単純な apply で掘り直さない条件を増やす。
- built-in skin への単純な apply は既存 snapshot を優先して解決し、外部 skin は同名更新・削除検知を優先して catalog load へ戻す。
- 外部 skin の明示 reload は現在定義を再確認し、同名 HTML/config 更新や削除を host prepare 前に拾う。
- 外部 skin の明示 reload で必要な catalog load は background へ逃がし、UI 側は結果 snapshot の採用と host prepare に集中させる。
- `header-reload` / `fallback-notice-retry` は `CatalogRefresh` として `dbinfo-*` より強く batch に残し、`minimal-chrome-reload` は cached definition の host 再準備に留める。
- 同期 `GetCurrentExternalSkinDefinition()` は cached snapshot 専用とし、catalog 再確認は `GetCurrentExternalSkinDefinitionAsync(...)` から `RefreshCurrentSkinDefinitionAsync()` へ流す。
- `SelectProfileValue(...)` を cold path に閉じ込め、session cache と persisted 値の責務差を明示する。

完了条件:
- skin 切り替え 1 回で、不要な catalog / DB / refresh の重なりがさらに減る。
- `skin-webview`、`skin-catalog`、`skin-db` を同じ trace で追える。

## Lane 7: rescue / repair 維持レーン

目的:
- rescue / repair を新規主戦場として広げず、通常動画テンポを壊さない範囲で維持・棚卸しする。

実施内容:
- repair が走った条件 / 走らなかった条件を観測し、動画固有名ではなく一般条件へ圧縮する。
- `No frames decoded` から救えた条件と救えなかった条件を整理する。
- UI 追加が必要でも、新ロジックを増やすより既存 rescue レーンの入口追加で留める。

完了条件:
- rescue の挙動を通常動画テンポと切り離して説明できる。
- rescue の変更が本線の hot path を重くしない。

## 6. 優先順位

実装順は次で固定する。

1. Lane 0 計測固定と作業差分保全
2. Lane 1 UIスレッド簡素化と入力優先
3. Lane 2 watch change set と diff-first UI の一本化
4. Lane 3 watcher / poll / shutdown 境界の安定化
5. Lane 3.5 `UiHang` オーバーレイ寿命管理の是正
6. Lane 4 起動 warm path の再短縮
7. Lane 5 visible-first / Player / 画像供給
8. Lane 6 `skin` 切り替え完全分離
9. Lane 7 rescue / repair 維持レーン

理由:
- 先に UI スレッドを塞ぐ経路と全面再評価構造を減らさないと、watch / skin / Player を個別最適化しても体感差が頭打ちになるため。

## 7. 直近の着手順

### 2026-05-26 見直し後の反映状況と次タスク

1. 完了: watch query-only / rename の `RefreshMovieViewFromCurrentSourceAsync(...)` に後着キャンセルを通し、`CancellationToken.None` で古い計算が走り切る経路を閉じた。
2. 完了: `TagControl` / `MainWindow.Tag` / `KanaBackfill` とサムネイルのみ削除に残っていた `Refresh()` / `Items.Refresh()` / `FilterAndSort(..., true)` を、局所反映・revision trigger・snapshot 予約へ寄せた。
3. 完了: 大件数時の通常 sort を background + revision guard へ寄せ、検索高速化後に残る UI スレッド滞在を減らした。
4. 完了: 起動 warm path は `first-page shown` / `input ready` / `heavy services started` を同一 revision / trigger / elapsed_ms で追えるようにした。
5. 完了: skin は DB 分離ではなく、`refresh` / `stale` / `catalog` / `navigate` 削減と `refresh end` の `elapsed_ms` / `catalog_*` / `persist_*` / `navigate_*` / `refresh_*_skipped` で完了判定できるログ基盤へ寄せた。
6. 完了: 物理動画削除後の `FilterAndSort(..., true)` は、DB 削除成功 ID だけを表示モデルから差分削除する局所反映へ寄せた。物理ファイル削除に失敗しても、既存挙動と同じく DB から消えた行を一覧正本として扱い、失敗は警告表示と watcher 復帰に委ねる。
7. 完了: visible-first は、viewport 一時未計測時に同一タブ・同一 source revision の preferred key snapshot を保持し、不要な画像再評価を抑えるようにした。
8. 完了: サブ5.5追加で、起動 fallback の `FilterAndSort(sortId, true)` は first-page 起動失敗時の DB 初期読込復旧、段階ロード中 sort 変更の `FilterAndSort(id.ToString(), true)` は新 sort の全件順序復旧として許容 fallback に分類した。Debug タブのサムネイル全削除後と Thumbnail 成功後の main tab 後段は DB 再読込をやめ、読み込み済み `MovieRecords` のサムネパス/ERROR 件数や visible refresh、preferred key revision、snapshot 予約へ寄せた。サムネERROR順だけは順序変化を拾うため、現在一覧の in-memory sort に限定する。
9. 完了: Bookmark タブの view refresh は `ObservableCollection` 通知へ任せ、ExtDetail / ExtensionDetail のタグ再描画は表示中の view-local 更新だけに絞った。
10. 次: 実機 `debug-runtime.log` で検索 / sort / watch / 起動 / skin の支配要因を確認し、残る UI 停滞が Player / 他の許容 fallback / 実機画像供給のどこにあるかを決める。

### Step 1

- 現在の未コミット差分を確認し、対象外ファイルを混ぜずに区切る。
- `layout*.xml` や Player / UpperTabs の実機操作差分がある時は、それを優先作業として扱うか、計画更新とは完全に分離する。

### Step 2

- UI スレッドを塞ぐ可能性が高い入口を、`search / reload / Player / watcher / thumbnail / skin` の順に棚卸しする。
- 背後処理を止める、遅らせる、queue 化する、snapshot だけ UI で取る、のどれで解くかを決める。

### Step 3

- `FilterAndSortAsync(...)` の呼び出し元を、`full snapshot reload`、`query only recompute`、`item diff apply` の 3 群へ再分類する。
- `changed paths + ChangeKind + DirtyFields + ObservedState` がある経路では、追加済みの full reload 戻り理由ログを使い、残った fallback 条件を実機ログから選んで縮める。

### Step 4

- watcher / poll / shutdown は、`input stop -> complete -> bounded drain -> timeout log` の順序を守る。
- low-update 時の poll interval 延長は、初期処理が落ち着いた後だけ有効化する。

### Step 5

- 起動 warm path と visible-first / Player のどちらを先に切るかを、`first-page shown`、`input ready`、`viewport request -> image ready`、Player 操作ログで決める。
- `skin` は別レーンとして、runtime bridge 境界固定と `refresh / catalog / DB` 分離を混ぜずに進める。

## 8. 受け入れ基準

1. 通常動画の初動を悪化させない。
2. watch 1 件追加時に全面 reload を常に踏まない。
3. 検索入力やページ移動の引っかかりが、既存ログ比較で改善している。
4. skin 切り替え時の refresh / catalog / DB のどこが効いたか分けて説明できる。
5. sidecar が壊れても起動不能にならない。
6. 後着検索・後着 reload で、古い filter / sort 計算が走り切らず、UI に反映されないことをログで説明できる。
7. 残る `Refresh()` / `Items.Refresh()` / `FilterAndSort(..., true)` は、許容 fallback か局所反映化対象か分類済みである。
8. 起動 / skin / visible-first は、実機ログで支配要因を説明できるまで完了扱いにしない。

## 9. 今回やらないこと

- `*.wb` のスキーマ変更
- IPC や別プロセス化の先行導入
- rescue / repair 条件の拡張を主目的にした高速化
- 仮想化パネル差し替えだけで解決したことにする議論

## 10. 関連資料

- `%USERPROFILE%\source\repos\IndigoMovieManager\AI向け_現在の全体プラン_workthree_2026-03-20.md`
- `%USERPROFILE%\source\repos\IndigoMovieManager\Docs\forAI\調査結果_UIボトルネック解消_2026-03-11.md`
- `%USERPROFILE%\source\repos\IndigoMovieManager\Docs\forAI\調査結果_watch_DB管理分離_UI詰まり防止_2026-03-20.md`
- `%USERPROFILE%\source\repos\IndigoMovieManager\Views\Main\Docs\Implementation Plan_大DB起動段階ロード化_2026-03-17.md`
- `%USERPROFILE%\source\repos\IndigoMovieManager\UpperTabs\Docs\Implementation Plan_ページUpDown引っかかり解消_2026-03-18.md`
- `%USERPROFILE%\source\repos\IndigoMovieManager\WhiteBrowserSkin\Docs\Implementation Plan_skin切り替え高速化_DB保存分離先行_2026-04-13.md`
