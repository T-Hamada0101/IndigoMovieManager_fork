using System.Windows.Input;

namespace IndigoMovieManager.BottomTabs.FileOrganizer
{
    internal static class FileOrganizerShortcutPolicy
    {
        // Ctrl単独かつ1～9だけを受理し、タブ非アクティブ時は必ず無効へ戻す。
        internal static int ResolveShortcutNumber(
            Key key,
            ModifierKeys modifiers,
            bool isFileOrganizerActive
        )
        {
            if (!isFileOrganizerActive || modifiers != ModifierKeys.Control)
            {
                return 0;
            }

            return key switch
            {
                >= Key.D1 and <= Key.D9 => (int)key - (int)Key.D0,
                >= Key.NumPad1 and <= Key.NumPad9 => (int)key - (int)Key.NumPad0,
                _ => 0,
            };
        }
    }
}
