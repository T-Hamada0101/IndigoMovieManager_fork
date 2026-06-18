namespace IndigoMovieManager
{
    internal readonly record struct MainDbSwitchSideEffectPlan(
        bool ShouldCloseMainMenu,
        bool ShouldPersistCurrentDbViewState,
        bool ShouldUpdateRecentFiles,
        bool ShouldRememberLastDoc,
        bool ShouldDiscardPreviousDbPendingThumbnailQueueItems
    );

    internal static class MainDbSwitchPlanPolicy
    {
        internal static MainDbSwitchSideEffectPlan BuildSideEffectPlan(
            MainWindow.MainDbSwitchSource source,
            bool hasCurrentDb,
            bool hasTargetDb,
            bool isDifferentDb
        )
        {
            bool isStartupAutoOpen = source == MainWindow.MainDbSwitchSource.StartupAutoOpen;
            bool hasSwitchablePair = hasCurrentDb && hasTargetDb && isDifferentDb;
            bool isUserVisibleSwitch =
                source == MainWindow.MainDbSwitchSource.New
                || source == MainWindow.MainDbSwitchSource.OpenDialog
                || source == MainWindow.MainDbSwitchSource.DragDrop
                || source == MainWindow.MainDbSwitchSource.RecentMenu;

            // 実際の保存・Recent更新・Queue掃除はMainWindowに残し、ここでは実行計画だけを返す。
            return new MainDbSwitchSideEffectPlan(
                ShouldCloseMainMenu: !isStartupAutoOpen,
                ShouldPersistCurrentDbViewState: isUserVisibleSwitch && hasSwitchablePair,
                ShouldUpdateRecentFiles: !isStartupAutoOpen,
                ShouldRememberLastDoc: !isStartupAutoOpen,
                ShouldDiscardPreviousDbPendingThumbnailQueueItems: hasSwitchablePair
            );
        }
    }
}
