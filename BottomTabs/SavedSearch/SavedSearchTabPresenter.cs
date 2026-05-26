using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Common;

namespace IndigoMovieManager.BottomTabs.SavedSearch
{
    // SavedSearch タブの表示更新と検索実行依頼だけを持ち、UI と本体検索をつなぐ。
    internal sealed class SavedSearchTabPresenter
    {
        private const string PreparingMessage = "保存済み検索条件は準備中です。";
        private const string EmptyMessage = "保存済み検索条件はありません。";

        private readonly LayoutAnchorable _tabHost;
        private readonly SavedSearchTabView _view;
        private readonly Func<string> _getDbFullPath;
        private readonly Func<string, Task<bool>> _executeSearchAsync;
        private bool _monitoringInitialized;
        private bool _viewHooked;
        private bool _isDirty;
        private SavedSearchItem[] _pendingItems = [];
        private string _pendingMessage = PreparingMessage;
        private int _reloadRevision;

        public SavedSearchTabPresenter(
            LayoutAnchorable tabHost,
            SavedSearchTabView view,
            Func<string> getDbFullPath,
            Func<string, Task<bool>> executeSearchAsync
        )
        {
            _tabHost = tabHost;
            _view = view;
            _getDbFullPath = getDbFullPath;
            _executeSearchAsync = executeSearchAsync;
        }

        // host の可視状態変化だけを拾い、表示可能時に未反映一覧を流し込む。
        public void Initialize()
        {
            if (!_monitoringInitialized && _tabHost != null)
            {
                _tabHost.PropertyChanged += OnTabHostPropertyChanged;
                _monitoringInitialized = true;
            }

            if (!_viewHooked && _view != null)
            {
                _view.SearchRequested += OnSearchRequested;
                _viewHooked = true;
            }

            ReloadItems();
            TryFlushIfVisible();
        }

        public void ReloadItems()
        {
            QueueReloadItems();
        }

        private void QueueReloadItems()
        {
            string dbFullPath = _getDbFullPath?.Invoke() ?? "";
            int requestRevision = Interlocked.Increment(ref _reloadRevision);

            ApplyItems([], PreparingMessage);
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            _ = RunReloadItemsAsync(dbFullPath, requestRevision);
        }

        private async Task RunReloadItemsAsync(string dbFullPath, int requestRevision)
        {
            try
            {
                // DB 読込は UI から切り離し、戻り時に最新要求だけを表示へ流す。
                SavedSearchItem[] items = await Task.Run(() =>
                    SavedSearchService.LoadItems(dbFullPath)
                );

                if (!IsCurrentReloadRequest(dbFullPath, requestRevision))
                {
                    return;
                }

                ApplyItems(items, items.Length > 0 ? "" : ResolveEmptyMessage(dbFullPath));
            }
            catch (Exception ex)
            {
                if (!IsCurrentReloadRequest(dbFullPath, requestRevision))
                {
                    return;
                }

                global::IndigoMovieManager.DebugRuntimeLog.Write(
                    "saved-search",
                    $"saved search reload failed: revision={requestRevision} db='{dbFullPath}' error={ex.Message}"
                );
                ApplyItems([], "保存済み検索条件の読込に失敗しました。");
            }
        }

        private bool IsCurrentReloadRequest(string dbFullPath, int requestRevision)
        {
            if (requestRevision != Volatile.Read(ref _reloadRevision))
            {
                return false;
            }

            string currentDbFullPath = _getDbFullPath?.Invoke() ?? "";
            return global::IndigoMovieManager.MainWindow.AreSameMainDbPath(
                dbFullPath,
                currentDbFullPath
            );
        }

        // 後で階層表示へ広げても、空状態表示の入口は presenter で固定する。
        public void ApplyPlaceholderText(string message = null)
        {
            ApplyItems([], string.IsNullOrWhiteSpace(message) ? PreparingMessage : message);
        }

        private void ApplyItems(SavedSearchItem[] items, string message)
        {
            _pendingItems = items ?? [];
            _pendingMessage = message ?? "";

            if (_view == null)
            {
                return;
            }

            if (!IsVisibleOrSelected())
            {
                MarkDirty();
                return;
            }

            _isDirty = false;
            _view.SetItems(_pendingItems, _pendingMessage);
        }

        private void OnTabHostPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!BottomTabActivationGate.ShouldReactToProperty(e?.PropertyName ?? ""))
            {
                return;
            }

            TryFlushIfVisible();
        }

        private bool IsVisibleOrSelected()
        {
            return BottomTabActivationGate.IsVisibleOrSelected(_tabHost);
        }

        private void MarkDirty()
        {
            _isDirty = true;
        }

        private void TryFlushIfVisible()
        {
            if (!_isDirty || !IsVisibleOrSelected() || _view == null)
            {
                return;
            }

            _isDirty = false;
            _view.SetItems(_pendingItems, _pendingMessage);
        }

        private async void OnSearchRequested(object sender, SavedSearchRequestedEventArgs e)
        {
            SavedSearchItem item = e?.Item;
            if (item == null || !item.CanExecute || _executeSearchAsync == null)
            {
                return;
            }

            await _executeSearchAsync(item.Contents);
        }

        private static string ResolveEmptyMessage(string dbFullPath)
        {
            return string.IsNullOrWhiteSpace(dbFullPath) ? PreparingMessage : EmptyMessage;
        }
    }
}
