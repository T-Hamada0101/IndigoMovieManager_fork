using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.BottomTabs.FileOrganizer
{
    public sealed class FileOrganizerDestinationItem : INotifyPropertyChanged
    {
        private string _folderPath = "";
        private bool _isShortcutEnabled;

        public FileOrganizerDestinationItem(int shortcutNumber)
        {
            ShortcutNumber = shortcutNumber;
        }

        public int ShortcutNumber { get; }

        public string ShortcutLabel => $"Ctrl+{ShortcutNumber}";

        public string FolderPath
        {
            get => _folderPath;
            set
            {
                string next = value ?? "";
                if (string.Equals(_folderPath, next, StringComparison.Ordinal))
                {
                    return;
                }

                _folderPath = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayFolderPath));
                OnPropertyChanged(nameof(IsRegistered));
                OnPropertyChanged(nameof(IsShortcutActive));
            }
        }

        public string DisplayFolderPath => IsRegistered ? FolderPath : "未登録";

        public bool IsRegistered => !string.IsNullOrWhiteSpace(FolderPath);

        public bool IsShortcutEnabled
        {
            get => _isShortcutEnabled;
            set
            {
                if (_isShortcutEnabled == value)
                {
                    return;
                }

                _isShortcutEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsShortcutActive));
            }
        }

        public bool IsShortcutActive => IsRegistered && IsShortcutEnabled;

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
