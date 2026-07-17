using System;

namespace IndigoMovieManager
{
    internal static class DockLayoutRestorePolicy
    {
        private const string ExtensionBottomTabContentId = "ToolExtension";
        private const string FileOrganizerBottomTabContentId = "ToolFileOrganizer";
        private const string BookmarkBottomTabContentId = "ToolBookmark";
        private const string SavedSearchBottomTabContentId = "ToolTagBar";
        private const string ThumbnailProgressContentId = "ToolThumbnailProgress";
        private const string TagEditorBottomTabContentId = "ToolTagEditor";
        private const string ThumbnailErrorBottomTabContentId = "ToolThumbnailError";
        private const string DebugToolContentId = "ToolDebug";
        private const string LogToolContentId = "ToolLog";

        internal static string FindMissingRequiredDockLayoutReason(
            string layoutText,
            bool shouldShowThumbnailErrorBottomTab,
            bool shouldShowDebugTab
        )
        {
            // 下部の常設タブは、誤って保存済みレイアウトから落ちても次回起動で救済する。
            (string ContentId, string Reason)[] requiredTabs =
            [
                (ExtensionBottomTabContentId, "missing-extension-bottom-tab"),
                (FileOrganizerBottomTabContentId, "missing-file-organizer-bottom-tab"),
                (BookmarkBottomTabContentId, "missing-bookmark-bottom-tab"),
                (SavedSearchBottomTabContentId, "missing-saved-search-bottom-tab"),
                (ThumbnailProgressContentId, "missing-thumbnail-progress"),
                (TagEditorBottomTabContentId, "missing-tag-editor-bottom-tab"),
            ];

            foreach ((string contentId, string reason) in requiredTabs)
            {
                if (!ContainsContentId(layoutText, contentId))
                {
                    return reason;
                }
            }

            if (
                ShouldRequireThumbnailErrorBottomTab(
                    layoutText,
                    shouldShowThumbnailErrorBottomTab
                )
            )
            {
                return "missing-thumbnail-error-bottom-tab";
            }

            // Debug 構成では開発用タブも必須扱いにして、古いレイアウトを引きずらない。
            if (shouldShowDebugTab && !ContainsContentId(layoutText, DebugToolContentId))
            {
                return "missing-debug-tool";
            }

            if (shouldShowDebugTab && !ContainsContentId(layoutText, LogToolContentId))
            {
                return "missing-log-tool";
            }

            return "";
        }

        internal static bool ShouldRequireThumbnailErrorBottomTab(
            string layoutText,
            bool shouldShowThumbnailErrorBottomTab
        )
        {
            if (!shouldShowThumbnailErrorBottomTab)
            {
                return false;
            }

            return !ContainsContentId(layoutText, ThumbnailErrorBottomTabContentId);
        }

        private static bool ContainsContentId(string layoutText, string contentId)
        {
            return (layoutText ?? "").Contains(
                $"ContentId=\"{contentId}\"",
                StringComparison.OrdinalIgnoreCase
            );
        }
    }
}
