using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace IndigoMovieManager.BottomTabs.FileOrganizer
{
    public partial class FileOrganizerTabView : UserControl
    {
        private FileOrganizerDestinationItem[] _items = [];

        public FileOrganizerTabView()
        {
            InitializeComponent();
        }

        public event EventHandler<FileOrganizerSlotEventArgs> RegisterRequested;

        public event EventHandler<FileOrganizerSlotEventArgs> ClearRequested;

        public event EventHandler<FileOrganizerSlotEventArgs> MoveAllRequested;

        public event EventHandler<FileOrganizerDetailActionEventArgs> DetailActionRequested;

        // MainWindow が保持する9件をそのまま表示し、View側に保存責務を持たせない。
        public void SetItems(IReadOnlyList<FileOrganizerDestinationItem> items)
        {
            _items = items?.ToArray() ?? [];
            DestinationItemsControl.ItemsSource = _items;
        }

        // 複数移動でも代表1件だけを表示し、件数は見出し横へ短く返す。
        public void SetSelectedMovie(MovieRecords movie, int targetCount = 1)
        {
            SelectedMovieDetailPanel.DataContext = movie;
            SelectedMovieDetailPanel.IsEnabled = movie != null;
            TargetCountTextBlock.Text = movie == null
                ? "未選択"
                : targetCount > 1
                    ? $"代表1件 / 対象 {targetCount} 件"
                    : "対象 1 件";
        }

        public void SetStatus(string message)
        {
            StatusTextBlock.Text = message ?? "";
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseSlotEvent(sender, RegisterRequested);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseSlotEvent(sender, ClearRequested);
        }

        private void MoveAllButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseSlotEvent(sender, MoveAllRequested);
        }

        private void CopyMoviePathButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseDetailAction(FileOrganizerDetailAction.CopyMoviePath);
        }

        private void CopyFolderPathButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseDetailAction(FileOrganizerDetailAction.CopyFolderPath);
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseDetailAction(FileOrganizerDetailAction.OpenFolder);
        }

        private void RaiseSlotEvent(
            object sender,
            EventHandler<FileOrganizerSlotEventArgs> eventHandler
        )
        {
            if ((sender as FrameworkElement)?.DataContext is not FileOrganizerDestinationItem item)
            {
                return;
            }

            eventHandler?.Invoke(this, new FileOrganizerSlotEventArgs(item.ShortcutNumber));
        }

        private void RaiseDetailAction(FileOrganizerDetailAction action)
        {
            DetailActionRequested?.Invoke(this, new FileOrganizerDetailActionEventArgs(action));
        }
    }

    public sealed class FileOrganizerSlotEventArgs : EventArgs
    {
        public FileOrganizerSlotEventArgs(int shortcutNumber)
        {
            ShortcutNumber = shortcutNumber;
        }

        public int ShortcutNumber { get; }
    }

    public enum FileOrganizerDetailAction
    {
        CopyMoviePath,
        CopyFolderPath,
        OpenFolder,
    }

    public sealed class FileOrganizerDetailActionEventArgs : EventArgs
    {
        public FileOrganizerDetailActionEventArgs(FileOrganizerDetailAction action)
        {
            Action = action;
        }

        public FileOrganizerDetailAction Action { get; }
    }
}
