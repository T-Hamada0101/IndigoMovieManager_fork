# Implementation Plan: プレイヤータブ右レール単純化 (2026-05-04)

最終更新日: 2026-05-05

変更概要:
- プレイヤータブ右レールを「再生対象を選ぶための軽い一覧」へ再定義する
- 右レール内の表示切り替え、説明文、重い詳細表示を段階的に削り、プレイヤー面積と選択テンポを優先する
- 既存の `Grid` サムネイル資産、選択同期、再生開始導線は維持し、WhiteBrowser DB とサムネイル保存先は変更しない
- 2026-05-04: 右レールを `PlayerThumbnailList` 1 系統の `VirtualizingWrapPanel` 表示へ統一し、詳細 1 列表示、切り替えボタン、`PlayerThumbnailCompactList`、下段レイアウト閾値を削除した
- 2026-05-04: Player タブ選択時の自動再生は `DispatcherPriority.ContextIdle` または短い上限待ちの後へ遅延し、タブ表示と選択同期を先に返すようにした
- 2026-05-05: 右レールの選択同期では、抑止中・非表示中の `SelectionChanged` から詳細/タグ更新を走らせず、表示中 Player の必要最小更新だけを明示的に行うようにした
- 2026-05-05: 右レールの visible-first 画像供給では、preferred 対象キーが空と確定した場合は off-screen 画像更新を通さず、未初期化状態や viewport 未計測状態だけ互換として従来どおり許可する方針を固定した
- 2026-05-05: preferred key 更新後は full reload や追加 `Refresh()` へ戻さず、軽い revision / trigger で実現済み画像 Binding を再評価する方針を固定した

## 1. 目的

- プレイヤータブの主役を左側の再生面へ戻し、右レールは動画選択だけに絞る。
- 右レールの XAML と選択同期コードを単純化し、将来の Player / UpperTabs 改修で触る面積を減らす。
- 右レール表示切り替え時のレイアウト再計算、裏側 `ListView` 同期、可視範囲 refresh を減らし、タブ切り替えと連続選択の体感テンポを守る。
- Player タブを開いた瞬間は動画初期化を始めず、タブ表示・選択・右レール描画を先に通す。
- `プレイヤー` タブは引き続き `Grid` サムネイルの代理表示として扱い、サムネイル生成・優先制御・保存先の正本を増やさない。

## 2. 現状問題

- `PlayerThumbnailHost` の中に 1 列詳細 `PlayerThumbnailList` と 3 列小サムネ `PlayerThumbnailCompactList` が共存しており、実体一覧が 2 系統になっている。
- 表示切り替えのたびに `SetPlayerThumbnailCompactViewMode(...)` が両方の `ListView`、トグル、選択、スクロール、可視範囲 refresh をまとめて扱うため、右レールのためだけに状態管理が太い。
- 1 列詳細表示は `ThumbPathGrid` の大きめ画像、動画名、長さを持つため、右レール幅 280px に対して情報量と描画コストが重い。
- 右レール上部に「Grid サムネ」「1列詳細と3列小サムネを切り替えできます」という説明があり、操作面としては意味が薄く、プレイヤー面積を圧迫している。
- 狭幅時は右レールを下段へ落としているが、下段でも 2 表示モードを持つため、レイアウト分岐と一覧分岐が重なっている。
- `GetUpperTabPlayerList()` が表示モードで対象を切り替えるため、選択取得、複数選択、スクロール、同期の読み解きに余計な条件が入っている。

## 3. 方針

- 右レールは単一のサムネイル一覧へ寄せる。既定案は 3 列相当の小サムネ一覧を残し、詳細 1 列表示と表示切り替えボタンは廃止する。
- 右レールは「選ぶ」だけに寄せる。動画名は短い補助表示までに留め、長さや説明テキストは原則置かない。
- `PlayerThumbnailList` / `PlayerThumbnailCompactList` の 2 系統状態を 1 系統へ畳み、選択同期 helper は単一リスト前提へ薄化する。
- プレイヤータブのサムネイル解決は引き続き `Grid` 扱いに正規化し、`ResolvePlayerTabGridProxyTabIndex(...)` の考え方は維持する。
- 右レールの画像供給は preferred 対象キーの状態を分ける。未初期化や viewport 未計測なら従来互換で off-screen 更新を許可し、空と確定した後は off-screen 更新を通さず可視・近傍の描画を優先する。
- preferred key 更新後の画像再評価は、右レール一覧の再構築ではなく、可視範囲 snapshot の軽い revision 更新、または Binding 用 trigger だけで起こす。生成済み画像を表示へ戻すために `FilterAndSort(..., true)`、full reload、追加 `Refresh()` を既定化しない。
- 左プレイヤー、WebView2 / MediaElement fallback、音量保存、fullscreen、手動サムネイル作成導線には触れない。
- DB write、サムネイル生成、Watcher、Queue、skin API へ影響を広げない。
- 実装時コメントは、日本語で処理の流れが分かるものだけ残す。単純化で不要になった説明コメントは削る。

## 4. 段階実装

### Phase 1: 表示仕様を固定する

- `PlayerThumbnailHost` の完成形を単一一覧に決める。
- 右レールの標準幅は現行 280px を上限目安にし、左プレイヤー面積を削らない。
- 残す情報はサムネイル、選択状態、必要最小限の動画名に限定する。
- コンテキストメニューと左クリック再生は維持する。
- 複数選択が Player 右レールで必要か確認し、不要なら単一選択化を候補にする。ただし既存操作と衝突する場合は `SelectionMode="Extended"` を維持する。

### Phase 2: XAML を 1 系統へ畳む

- 完了: `Views/Main/MainWindow.xaml` の `PlayerThumbnailHost` から表示切り替えトグルと説明文を外した。
- 完了: compact 側の軽い見た目を `PlayerThumbnailList` へ正本化し、`PlayerThumbnailCompactList` は削除した。
- 完了: `VirtualizingWrapPanel`、固定 item 幅、`VirtualizationMode="Recycling"` は維持し、右レールスクロールの軽さを守った。
- 完了: 画像 binding は `ThumbPathGrid` と `upperTabImageSourceConverter` を維持し、表示中判定の `x:Reference` は単一一覧へ差し替えた。
- 完了: XAML 上の大きな 1 列カード、動画長表示、切り替え UI を削り、レールを軽いサムネイル strip として読む形へ寄せた。

### Phase 3: PlayerTab コードを薄くする

- 完了: `UpperTabs/Player/MainWindow.UpperTabs.PlayerTab.cs` の `_isPlayerThumbnailCompactViewEnabled` と表示切り替え handler を削った。
- 完了: `GetUpperTabPlayerList()` は単一 `ListView` を返すだけにした。
- 完了: `GetAllUpperTabPlayerLists()` は単一リストだけを返す形へ薄化し、`SetPlayerThumbnailCompactViewMode(...)` は削除した。
- `PlayerThumbnailList_SelectionChanged(...)` は、選択された 1 件を左プレイヤーへ開く流れだけにする。
- `SelectUpperTabPlayerMovieRecord(...)` は単一リスト選択と必要時の `ScrollIntoView(...)` だけへ縮める。
- 完了: `RequestUpperTabVisibleRangeRefresh(..., reason: "player-view-mode")` は表示モード廃止に合わせて削除した。
- 完了: `PlayerThumbnailList_SelectionChanged(...)` は、抑止中や Player 非表示中なら詳細/タグ更新へ進まない。抑止付きの選択同期で詳細が必要な場合は `SyncUpperTabPlayerSelection(...)` 側で表示中 Player の最小更新だけ行う。

### Phase 4: レイアウト分岐を見直す

- 完了: `UpdatePlayerTabLayoutMode()` は右固定レールだけに寄せ、下段落としの閾値 `PlayerTabBottomLayoutWidthThreshold` を削除した。
- 完了: 右レール幅は 264px、隙間は 12px に縮め、左プレイヤー面積を広げた。
- 今後、狭幅で重なりが出る場合も旧 2 系統へ戻さず、単一一覧の item 幅、右レール幅、または最小ウィンドウ制約で調整する。

### Phase 5: 仕上げとドキュメント整合

- 完了: `UpperTabs/Player/Docs/Implementation Plan_プレイヤータブ追加_2026-04-24.md` の右側サムネ一覧説明を、新仕様へ更新した。
- 必要なら本書の変更概要へ完了内容を追記する。
- 関連 AI 向け資料へ影響がある場合だけ、更新日付きで変更概要を残す。

### Phase 6: タブ選択時の自動再生を後段化する

- 完了: `HandleUpperTabPlayerSelectionChanged(...)` はタブ選択ログを出した後、直接 `OpenMovieInPlayerTabAsync(...)` を待たずに自動再生予約だけを積む。
- 完了: 予約再生は `DispatcherPriority.ContextIdle` または 250ms 上限待ちの後に実行し、idle が来ない時も再生開始が無期限に遅れないようにする。
- 完了: 予約番号、Player タブ選択状態、現在選択レコードを確認して古い要求を捨てる。選択レコードは `ReferenceEquals` だけでなく `Movie_Id` と `Movie_Path` でも確認し、一覧再構築後の同一動画を拾えるようにする。
- 完了: Player タブ活性化中の先頭選択は `SelectionChanged` 再生を抑止し、初回未選択時も自動再生予約をすり抜けないようにする。
- 完了: Player タブを離れる時は予約番号を進め、後着の自動再生が裏で始まらないようにする。
- 維持: 右レールをクリックした時、手動サムネイル導線から Player へ飛ばす時、明示的な再生要求は従来どおり即時に `OpenMovieInPlayerTabAsync(...)` へ流す。

## 5. 検証

- Visual Studio 2026 または MSBuild で `x64` ビルドが通ること。
- プレイヤータブを開いた時、先頭または選択中動画が左プレイヤーへ同期されること。
- 右レールで別動画をクリックした時、WebView2 対象拡張子と MediaElement fallback 対象拡張子の両方で再生開始できること。
- 右レールの連続クリックで UI が固まらず、`debug-runtime.log` の `tab change end` が極端に悪化しないこと。
- 右レールのスクロールでサムネイルが遅延表示されても、画像 decode が表示中項目へ限定されること。
- preferred 対象キーが空と確定した状態では off-screen 画像更新が走らず、未初期化状態や viewport 未計測状態では従来互換の表示更新が維持されること。
- preferred key 更新後の実現済み画像 Binding 再評価が軽い revision / trigger で完結し、右レールの full reload や追加 `Refresh()` を呼ばないこと。
- 狭幅時も右固定レールのまま、左プレイヤー、サムネイル一覧、操作バーが重ならないこと。
- コンテキストメニュー、手動サムネイル取得、ブックマーク追加、音量保存、fullscreen が退行していないこと。
- `git diff --check` で空白エラーがないこと。
- `git status --short` で作業対象外の既存差分を混ぜていないこと。

## 6. ロールバック方針

- 退行が出た場合は、まず `PlayerThumbnailHost` と `MainWindow.UpperTabs.PlayerTab.cs` の右レール単純化差分だけを戻す。
- 左プレイヤー、WebView2、fullscreen、音量保存の差分とは混ぜず、戻す単位を右レールに限定する。
- 単一一覧で性能や操作性が悪化した場合は、旧 1 列詳細表示を戻すのではなく、単一一覧の item サイズ、列数、decode profile、右レール幅を先に調整する。
- どうしても表示モード切り替えが必要と判断した場合は、2 つの `ListView` を持ち直す前に、単一 `ItemsControl` 内の item template / panel 切り替えで済むかを検討する。
- WhiteBrowser DB、サムネイル保存先、`Grid` 代理タブの正規化はロールバック対象に含めない。

## 7. 関連ファイル

- `Views/Main/MainWindow.xaml`
- `UpperTabs/Player/MainWindow.UpperTabs.PlayerTab.cs`
- `UpperTabs/Player/Docs/Implementation Plan_プレイヤータブ追加_2026-04-24.md`
