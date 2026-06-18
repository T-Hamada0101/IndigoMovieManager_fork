using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 左ドロワー表示中だけ、watch の新規流入を抑えて操作テンポを守る。
        private void MenuToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            BeginWatchUiSuppression("left-drawer");
            SetWebViewPlayerHiddenForLeftDrawer(hidden: true);
        }

        // 左ドロワーを閉じた時だけ、保留があれば watch を1回 catch-up させる。
        private void MenuToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            SetWebViewPlayerHiddenForLeftDrawer(hidden: false);
            EndWatchUiSuppression("left-drawer");
        }

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
        /// 段階ロード中は全件再取得付き FilterAndSort、通常時はインメモリ SortData で並び替えて先頭を選択する。
        /// </summary>
        private async void ComboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }
            if (_suppressSortComboSelectionChangedHandling)
            {
                return;
            }
            if (sender is ComboBox senderObj)
            {
                if (MainVM.MovieRecs.Count > 0)
                {
                    if (senderObj.SelectedValue != null)
                    {
                        var id = senderObj.SelectedValue;
                        bool shouldSelectFirstItem = true;
                        if (IsStartupFeedPartialActive)
                        {
                            FilterAndSort(id.ToString(), true);
                        }
                        else
                        {
                            shouldSelectFirstItem = await SortDataAsync(id.ToString());
                        }
                        if (id.ToString() == "28")
                        {
                            RefreshThumbnailErrorRecords(force: true);
                        }
                        if (shouldSelectFirstItem)
                        {
                            SelectFirstItem();
                        }
                    }
                }
            }
        }
    }
}
