using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private bool _suppressSearchBoxTextChangedHandling = false;
        private SearchExecutionController _searchExecutor;
        private long _searchHistoryRefreshStamp;
        private bool _searchInputPriorityActive;
        private bool _searchInputShutdownHooked;

        // =================================================================================
        // 検索に関する UI イベント処理 (View層のロジック)
        // ユーザーがUI画面（SearchBox等のコントロール）で行った操作を受け取り、
        // 最終的に ViewModel(MainVM) 側の絞り込み/検索処理へ委譲する導線となる。
        // =================================================================================

        /// <summary>
        /// 検索コンボボックスの選択が切り替わった瞬間のイベントだぜ！🎯
        /// </summary>
        private void SearchBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }
            if (_suppressSearchBoxTextChangedHandling)
            {
                // 履歴候補の差し替え中に発生した選択変更は、ユーザー選択として扱わない。
                _searchBoxItemSelectedByUser = false;
                return;
            }

            // ドロップダウンが開いている状態でユーザーが選択を変更した（マウス・キー操作等）場合のみ、
            // 「ユーザー起因の検索」としてフラグを立てて後続処理(DropDownClosed等)での実行を促す。
            if (SearchBox.IsDropDownOpen)
            {
                _searchBoxItemSelectedByUser = true;
            }

            if (e.Source is ComboBox)
            {
                // [MVVM移行メモ]
                // 以前はここで即時フィルタ(FilterAndSort等)を走らせていたが、
                // 挙動が重くなる・意図しないタイミングで走る問題があるため無効化している。
                /*
                FilterAndSort(MainVM.DbInfo.Sort);  //サーチのコンボチェンジイベント。
                SelectFirstItem();
                if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword))
                {
                    //セレクションが変わってもHistoryに書いてるかも。
                    InsertHistoryTable(MainVM.DbInfo.DBFullPath, MainVM.DbInfo.SearchKeyword);
                }
                */
            }
        }

        /// <summary>
        /// ドロップダウンの履歴にマウスが乗ったら、自動で「それな！」と選択状態にしてやる超親切処理！✨
        /// </summary>
        private void SearchBoxItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is ComboBoxItem item && item.IsMouseOver)
            {
                item.IsSelected = true;
            }
        }

        /// <summary>
        /// 検索ボックスからフォーカスが外れた時が勝負！今のキーワードを「今回の実績」としてDBの歴史に深く刻み込むぜ！🛡️
        /// </summary>
        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // フォーカス移動後も保留中の検索は維持し、入力中だけの優先参照を手放す。
            ReleaseSearchInputPriority();

            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }

            if (Tabs.SelectedItem == null)
            {
                return;
            }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(MainVM.DbInfo.SearchKeyword))
            {
                // フォーカス離脱時の実績記録は背景へ送り、UIイベントをDB I/Oで止めない。
                QueueSearchHistoryUsageRecord(
                    MainVM.DbInfo.DBFullPath,
                    MainVM.DbInfo.SearchKeyword
                );
            }
        }

        /// <summary>
        /// おっと検索テキストに変更があったな！？すかさず状態をキャッチするイベントだ！👀
        /// </summary>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }
            if (_suppressSearchBoxTextChangedHandling)
            {
                return;
            }
            // IME入力確定前のフリックや変換中の文字では処理を走らせない
            if (_imeFlag)
            {
                return;
            }

            if (e.Source is ComboBox combo)
            {
                var text = combo.Text;
                EnsureSearchInputPriority();

                // [MVVM移行メモ]
                // 以前搭載されていた1文字ごとのインクリメンタルサーチ処理。
                // テキスト変化のたびにDBやリストを走査するため負荷が高く「美しくない」と判断され、現在無効化されている。
                /* インクリメントサーチ部。一旦コメントアウト。
                // 入力文字列の末尾が -, |, { のいずれかならサーチしない。}は終了なので、サーチスタート。
                if (!string.IsNullOrEmpty(text))
                {
                    // すでに{があり、}がまだ無い場合はreturn
                    int openIdx = text.IndexOf('{');
                    int closeIdx = text.IndexOf('}');
                    if (openIdx >= 0 && (closeIdx < 0 || closeIdx < openIdx))
                    {
                        return;
                    }

                    char lastChar = text[^1];
                    if (lastChar == '-' || lastChar == '|' || lastChar == '{')
                    {
                        return;
                    }
                }
                //インクリメンタルサーチがなぁ。ちょっと間隔で調整的な。美しくない。
                DateTime now = DateTime.Now;
                TimeSpan timeSinceLastUpdate = now - _lastInputTime;

                if (timeSinceLastUpdate >= _timeInputInterval)
                {
                    _lastInputTime = now;
                    FilterAndSort(MainVM.DbInfo.Sort);  //サーチのテキストチェンジイベント。
                    SelectFirstItem();
                }
                */

                // 唯一有効なのは、テキスト入力が完全に消された(空になった)場合。
                // 絞り込みを解除し、全件表示へ戻す。
                if (string.IsNullOrEmpty(text))
                {
                    CancelIncrementalSearchDebounce();
                    // 検索解除も検索正本へ合流し、一覧反映を先に返してからサムネ常駐を再起動する。
                    _ = ExecuteSearchKeywordFromInputAsync(text);
                    return;
                }

                // 通常時だけ debounce で検索確定し、連打入力でも UI を詰まらせにくくする。
                QueueIncrementalSearch(text);
            }
        }

        /// <summary>
        /// ドロップダウンが閉じた時、ユーザーが「これだ！」と選んだ履歴なら爆速で検索を走らせる！🏃‍♂️
        /// </summary>
        private void SearchBox_DropDownClosed(object sender, EventArgs e)
        {
            if (_searchBoxItemSelectedByUser)
            {
                DoSearchBoxSearch();
                _searchBoxItemSelectedByUser = false;
            }
        }

        /// <summary>
        /// 履歴をマウスで直撃クリックしたな！「ユーザーの強い意志」としてフラグを力強く立てるぜ！🚩
        /// </summary>
        private void SearchBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _searchBoxItemSelectedByUser = true;
        }

        /// <summary>
        /// 検索ボックスにカーソルがある時のキーボード入力監視網！エンターキーを打つ隙は逃さない！🔫
        /// </summary>
        private async void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }
            if (_imeFlag)
            {
                return;
            }

            if (e.Source is ComboBox combo)
            {
                // 例外操作: 履歴を開いている最中に Deleteキー が押されたら、その履歴エントリーを消去する
                if (
                    e.Key == Key.Delete
                    && combo.IsDropDownOpen
                    && combo.SelectedItem is History selectedHistory
                )
                {
                    int idx = combo.SelectedIndex;

                    // まずViewModelから即座に消すことでUIの反応を良く見せる
                    MainVM.HistoryRecs.Remove(selectedHistory);

                    // 実際のDBからの履歴データ削除は少し重いためバックグラウンドで処理
                    await Task.Run(() =>
                        SearchHistoryService.DeleteHistoryEntry(
                            MainVM.DbInfo.DBFullPath,
                            selectedHistory.Find_Id
                        )
                    );

                    // 削除後にカーソルが消えないよう、次のアイテムにフォーカスを当てる処理
                    if (MainVM.HistoryRecs.Count > 0)
                    {
                        if (idx >= MainVM.HistoryRecs.Count)
                        {
                            idx = MainVM.HistoryRecs.Count - 1;
                        }
                        combo.SelectedIndex = idx;
                    }

                    // Deleteキーが文字入力欄の1文字削除などへ誤爆しないようブロック
                    e.Handled = true;
                    return;
                }

                // 通常の検索実行: Enterキー で検索を確定し、必要なら履歴も同期する
                if (e.Key == Key.Enter)
                {
                    // Enter は既定ボタンへ流さず、検索ボックス起点の共通入口へ揃える。
                    CancelIncrementalSearchDebounce(releaseInputPriority: false);
                    _searchBoxItemSelectedByUser = false;
                    if (combo.IsDropDownOpen)
                    {
                        combo.IsDropDownOpen = false;
                    }

                    e.Handled = true;

                    string enteredText = combo.Text ?? "";
                    bool searchExecuted = await ExecuteSearchKeywordFromInputAsync(enteredText);
                    if (!searchExecuted)
                    {
                        return;
                    }

                    // Editable ComboBox の KeyDown 連鎖が一段落してから履歴同期する。
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    PersistSearchHistoryAfterSearch(enteredText);
                    return;
                }
            }
        }

        /// <summary>
        /// いよいよ検索処理の本丸への突撃！入力キーワードをViewModelに叩き込み、後続のフィルタ部隊を全軍突撃させるぞ！⚔️🔥
        /// </summary>
        private void DoSearchBoxSearch()
        {
            CancelIncrementalSearchDebounce(releaseInputPriority: false);
            _ = ExecuteSearchKeywordFromInputAsync(SearchBox?.Text ?? "");
        }

        // Enter 確定後だけ履歴保存をまとめ、検索結果ゼロや空白入力は静かに流す。
        private void PersistSearchHistoryAfterSearch(string text)
        {
            string keyword = text ?? "";
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            int searchCount = MainVM?.DbInfo?.SearchCount ?? 0;
            QueueSearchHistoryRefresh(dbFullPath, keyword, searchCount);
        }

        private void QueueSearchHistoryRefresh(
            string dbFullPath,
            string keyword,
            int searchCount
        )
        {
            if (
                string.IsNullOrWhiteSpace(dbFullPath)
                || string.IsNullOrWhiteSpace(keyword)
                || searchCount <= 0
            )
            {
                return;
            }

            long refreshStamp = Interlocked.Increment(ref _searchHistoryRefreshStamp);
            _ = RefreshSearchHistoryAsync(dbFullPath, keyword, searchCount, refreshStamp);
        }

        private async Task RefreshSearchHistoryAsync(
            string dbFullPath,
            string keyword,
            int searchCount,
            long refreshStamp
        )
        {
            History[] records;
            try
            {
                // DB書き込みと再読込は検索確定後のUI導線から外し、反映だけ後で戻す。
                records = await Task.Run(
                        () =>
                        {
                            SearchHistoryService.PersistSuccessfulSearch(
                                dbFullPath,
                                keyword,
                                searchCount
                            );
                            return SearchHistoryService.LoadLatestHistory(dbFullPath);
                        }
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "search-history",
                    $"history refresh failed: db='{dbFullPath}' keyword='{keyword}' err='{ex.Message}'"
                );
                return;
            }

            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                await Dispatcher
                    .InvokeAsync(
                        () =>
                        {
                            if (
                                refreshStamp != _searchHistoryRefreshStamp
                                || !AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? "")
                            )
                            {
                                return;
                            }

                            ApplySearchHistoryRecords(records, SearchBox?.Text ?? "");
                        },
                        DispatcherPriority.Background
                    )
                    .Task.ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (
                Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished
            )
            {
            }
            catch (InvalidOperationException) when (
                Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished
            )
            {
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "search-history",
                    $"history apply failed: db='{dbFullPath}' keyword='{keyword}' err='{ex.Message}'"
                );
            }
        }

        private void ApplySearchHistoryRecords(IEnumerable<History> historyRecords, string currentText)
        {
            bool previousSuppressState = _suppressSearchBoxTextChangedHandling;
            _suppressSearchBoxTextChangedHandling = true;
            try
            {
                historyData = null;
                ApplySearchHistoryRecordItems(historyRecords);

                // 履歴再読込で編集中テキストが消えないように戻す。
                string normalizedCurrentText = currentText ?? "";
                if (
                    SearchBox != null
                    && !string.Equals(
                        SearchBox.Text ?? "",
                        normalizedCurrentText,
                        StringComparison.Ordinal
                    )
                )
                {
                    SearchBox.Text = normalizedCurrentText;
                }
            }
            finally
            {
                _suppressSearchBoxTextChangedHandling = previousSuppressState;
            }
        }

        private void ApplySearchHistoryRecordItems(IEnumerable<History> historyRecords)
        {
            History[] nextRecords = [.. (historyRecords ?? []).Where(item => item != null)];
            if (AreSameSearchHistoryRecords(MainVM.HistoryRecs, nextRecords))
            {
                return;
            }

            MainVM.HistoryRecs.Clear();
            foreach (History item in nextRecords)
            {
                MainVM.HistoryRecs.Add(item);
            }
        }

        private static bool AreSameSearchHistoryRecords(
            IReadOnlyList<History> currentRecords,
            IReadOnlyList<History> nextRecords
        )
        {
            if (currentRecords == null || nextRecords == null || currentRecords.Count != nextRecords.Count)
            {
                return false;
            }

            for (int i = 0; i < nextRecords.Count; i++)
            {
                History current = currentRecords[i];
                History next = nextRecords[i];
                if (
                    current == null
                    || next == null
                    || current.Find_Id != next.Find_Id
                    || !string.Equals(current.Find_Text, next.Find_Text, StringComparison.Ordinal)
                    || !string.Equals(current.Find_Date, next.Find_Date, StringComparison.Ordinal)
                )
                {
                    return false;
                }
            }

            return true;
        }

        private void QueueSearchHistoryUsageRecord(string dbFullPath, string keyword)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            _ = Task.Run(
                () =>
                {
                    try
                    {
                        SearchHistoryService.RecordSearchUsage(dbFullPath, keyword);
                    }
                    catch (Exception ex)
                    {
                        DebugRuntimeLog.Write(
                            "search-history",
                            $"history usage record failed: db='{dbFullPath}' keyword='{keyword}' err='{ex.Message}'"
                        );
                    }
                }
            );
        }

        // 検索 UI が複数になっても、本体検索の入口は 1 つへ寄せる。
        private async Task<bool> ExecuteSearchKeywordAsync(
            string text,
            bool syncSearchBoxText,
            string triggerReason = "search"
        )
        {
            UiOperationSnapshot snapshot = CaptureUserPriorityOperationSnapshot(
                IsUserPriorityWorkActive(),
                isManualMode: false
            );
            DebugRuntimeLog.Write(
                "ui-priority",
                BuildUiShellInputLogMessage("search", triggerReason, snapshot)
            );

            return await SearchExecutor.ExecuteAsync(text, syncSearchBoxText);
        }

        // 外部スキン検索は SearchBox を同期しつつ、本体検索だけを再利用する。
        private async Task<bool> ExecuteExternalSkinSearchAsync(string text)
        {
            bool executed = await ExecuteSearchKeywordAsync(text, true);
            if (executed)
            {
                PersistSearchHistoryAfterSearch(text);
            }

            return executed;
        }

        private void UpdateSearchBoxTextWithoutSideEffects(string text)
        {
            if (SearchBox == null)
            {
                return;
            }

            string normalizedText = text ?? "";
            if (string.Equals(SearchBox.Text ?? "", normalizedText, StringComparison.Ordinal))
            {
                return;
            }

            _suppressSearchBoxTextChangedHandling = true;
            try
            {
                SearchBox.Text = normalizedText;
            }
            finally
            {
                _suppressSearchBoxTextChangedHandling = false;
            }
        }

        private SearchExecutionController SearchExecutor =>
            _searchExecutor ??= new SearchExecutionController(
                getDbFullPath: () => MainVM?.DbInfo?.DBFullPath ?? "",
                getSortId: () => MainVM?.DbInfo?.Sort ?? "",
                setSearchKeyword: keyword => MainVM.DbInfo.SearchKeyword = keyword,
                syncSearchBoxText: UpdateSearchBoxTextWithoutSideEffects,
                beginUserPriorityWork: BeginUserPriorityWork,
                endUserPriorityWork: EndUserPriorityWork,
                restartThumbnailTask: RestartThumbnailTask,
                refreshSearchResultsAsync: RefreshSearchResultsAsync,
                selectFirstItem: SelectFirstSearchResultIfNeeded
            );

        // 検索後も現在選択が残っていれば維持し、未選択になった時だけ従来の先頭選択へ戻す。
        private void SelectFirstSearchResultIfNeeded()
        {
            if (!ShouldSelectFirstSearchResult(GetSelectedItemByTabIndex()))
            {
                return;
            }

            SelectFirstItem();
        }

        internal static bool ShouldSelectFirstSearchResult(MovieRecords selectedItem)
        {
            return selectedItem == null;
        }

        // 検索確定は通常時は query-only で軽く流し、起動直後の部分ロード中だけ full reload を維持する。
        private Task RefreshSearchResultsAsync(string sortId)
        {
            bool shouldReload = IsStartupFeedPartialActive;
            return FilterAndSortAsync(sortId, shouldReload);
        }

        public async Task ApplySearchKeywordFromLinkAsync(string keyword)
        {
            // タグ・詳細・ブックマークリンク検索を検索正本へ合流させ、
            // 通常時のDB再読込を避ける。
            try
            {
                await ExecuteSearchKeywordAsync(keyword ?? "", true, "link-search");
                // 既に検索欄にフォーカスがある時は再要求しない。
                if (SearchBox != null && !SearchBox.IsKeyboardFocusWithin)
                {
                    SearchBox.Focus();
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"link search failed: {ex.GetType().Name}: {ex.Message}"
                );
            }
        }

        // 変換途中の記号入力や部分ロード中は既存の確定検索へ寄せ、通常時だけ debounce で流す。
        private void QueueIncrementalSearch(string text)
        {
            if (!CanRunIncrementalSearch(text))
            {
                CancelIncrementalSearchDebounce();
                return;
            }

            StopDispatcherTimerSafely(_searchInputDebounceTimer, nameof(_searchInputDebounceTimer));
            TryStartDispatcherTimer(_searchInputDebounceTimer, nameof(_searchInputDebounceTimer));
        }

        // Enter や履歴選択と二重発火しないよう、保留中の debounce 検索を止める。
        private void CancelIncrementalSearchDebounce(bool releaseInputPriority = true)
        {
            StopDispatcherTimerSafely(_searchInputDebounceTimer, nameof(_searchInputDebounceTimer));
            if (releaseInputPriority)
            {
                ReleaseSearchInputPriority();
            }
        }

        // タイピング開始から検索本体開始まで背後のUI更新を後ろへ送り、入力描画を優先する。
        private void EnsureSearchInputPriority()
        {
            if (_searchInputPriorityActive)
            {
                return;
            }

            _searchInputPriorityActive = true;
            BeginUserPriorityWork("search-input");
            if (!_searchInputShutdownHooked && Dispatcher != null)
            {
                Dispatcher.ShutdownStarted += SearchInputDispatcher_ShutdownStarted;
                _searchInputShutdownHooked = true;
            }
        }

        private void ReleaseSearchInputPriority()
        {
            if (!_searchInputPriorityActive)
            {
                return;
            }

            _searchInputPriorityActive = false;
            EndUserPriorityWork("search-input");
        }

        private void SearchInputDispatcher_ShutdownStarted(object sender, EventArgs e)
        {
            StopDispatcherTimerSafely(_searchInputDebounceTimer, nameof(_searchInputDebounceTimer));
            ReleaseSearchInputPriority();
        }

        // 検索本体がuser-priorityを取得してから入力中の1参照を渡し、抑止の隙間を作らない。
        private async Task<bool> ExecuteSearchKeywordFromInputAsync(string text)
        {
            Task<bool> searchTask;
            try
            {
                searchTask = ExecuteSearchKeywordAsync(text, false);
            }
            finally
            {
                ReleaseSearchInputPriority();
            }

            return await searchTask;
        }

        // 起動直後の full reload 連打と、未完成な特殊構文の途中評価を避ける。
        private bool CanRunIncrementalSearch(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (IsStartupFeedPartialActive)
            {
                return false;
            }

            int openIdx = text.IndexOf('{');
            int closeIdx = text.IndexOf('}');
            if (openIdx >= 0 && (closeIdx < 0 || closeIdx < openIdx))
            {
                return false;
            }

            char lastChar = text[^1];
            return lastChar != '-' && lastChar != '|' && lastChar != '{';
        }

        // タイピングが一段落した時だけ、現在テキストを query-only 検索へ流す。
        private async void SearchInputDebounceTimer_Tick(object sender, EventArgs e)
        {
            CancelIncrementalSearchDebounce(releaseInputPriority: false);

            if (_imeFlag || SearchBox == null)
            {
                ReleaseSearchInputPriority();
                return;
            }

            string text = SearchBox.Text ?? "";
            if (!CanRunIncrementalSearch(text))
            {
                ReleaseSearchInputPriority();
                return;
            }

            if (string.Equals(MainVM.DbInfo.SearchKeyword ?? "", text, StringComparison.Ordinal))
            {
                ReleaseSearchInputPriority();
                return;
            }

            await ExecuteSearchKeywordFromInputAsync(text);
        }
    }
}
