using System.Windows.Controls;
using System.Windows.Input;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // IME確定時に検索入力フラグを通常状態へ戻す。
        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            _imeFlag = false;
        }

        // IME変換開始を検知して検索の即時実行を抑制する。
        private void OnPreviewTextInputStart(object sender, TextCompositionEventArgs e)
        {
            _imeFlag = true;
        }

        // IME変換文字が空になったら検索入力フラグを解除する。
        private void OnPreviewTextInputUpdate(object sender, TextCompositionEventArgs e)
        {
            if (e.TextComposition.CompositionText.Length == 0)
            {
                _imeFlag = false;
            }
        }

        /// <summary>
        /// 一覧タブ上のショートカットキー（Enter/F6/C/V/+/-/F2/F12/Delete等）を
        /// 各機能ハンドラへ振り分けるキーディスパッチャ。
        /// </summary>
        private void Tab_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Tabs.SelectedIndex == -1)
            {
                return;
            }
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            if (TryHandleUpperTabPageScroll(e))
            {
                return;
            }

            if (e.Key == Key.Delete)
            {
                // Delete系ショートカットは修飾キーごとに別設定へ振り分ける。
                if (TryHandleDeleteShortcut(e))
                {
                    return;
                }
            }

            switch (e.Key)
            {
                case Key.Enter: //再生
                    PlayMovie_Click(sender, e);
                    break;
                case Key.F6: //タグ編集
                    TagEdit_Click(sender, e);
                    break;
                case Key.C: //タグのコピー
                    TagCopy_Click(sender, e);
                    break;
                case Key.V: //タグの貼り付け
                    TagPaste_Click(sender, e);
                    break;
                case Key.Add: //スコアプラス
                case Key.Subtract: //スコアマイナス
                    MenuScore_Click(sender, e);
                    break;
                case Key.F2: //名前の変更
                    RenameFile_Click(sender, e);
                    break;
                case Key.F12: //親フォルダ
                    OpenParentFolder_Click(sender, e);
                    break;
                case Key.P: //プロパティ
                    break;
                default:
                    return;
            }
        }

        /// <summary>
        /// ソートコンボボックスの選択変更ハンドラ。
        /// 段階ロード中は全件再取得付き FilterAndSort、通常時は選択を保ったままインメモリ SortData で並び替える。
        /// </summary>
        private async void ComboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox senderObj)
            {
                return;
            }

            string dbFullPath = MainVM.DbInfo.DBFullPath;
            bool isSelectionChangeSuppressed = _suppressSortComboSelectionChangedHandling;
            int movieCount =
                string.IsNullOrEmpty(dbFullPath) || isSelectionChangeSuppressed
                    ? 0
                    : MainVM.MovieRecs.Count;
            string selectedSortId = movieCount > 0 ? senderObj.SelectedValue?.ToString() : null;
            SortComboSelectionPlan plan = SortComboSelectionPolicy.BuildPlan(
                dbFullPath,
                isSelectionChangeSuppressed,
                movieCount,
                selectedSortId,
                IsStartupFeedPartialActive
            );
            if (!plan.ShouldHandle)
            {
                return;
            }

            UiOperationSnapshot snapshot = CaptureUserPriorityOperationSnapshot(
                IsUserPriorityWorkActive(),
                isManualMode: false
            );
            DebugRuntimeLog.Write(
                "ui-priority",
                BuildUiShellInputLogMessage("sort", "combo-selection-changed", snapshot)
            );

            BeginUserPriorityWork("sort");
            try
            {
                if (plan.ShouldUseStartupFullReload)
                {
                    FilterAndSort(plan.SortId, true);

                    if (plan.ShouldRefreshThumbnailErrorRecords)
                    {
                        RefreshThumbnailErrorRecords(force: true);
                    }

                    // 起動時の全件順序復旧だけは、従来どおり先頭選択へ戻す。
                    SelectFirstItem();
                    return;
                }

                if (!await SortDataAsync(plan.SortId))
                {
                    return;
                }

                if (plan.ShouldRefreshThumbnailErrorRecords)
                {
                    RefreshThumbnailErrorRecords(force: true);
                }
            }
            finally
            {
                EndUserPriorityWork("sort");
            }
        }
    }
}
