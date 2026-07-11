using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly record struct MovieViewFocusContext(
            int TabIndex,
            MovieViewFocusAnchor Anchor
        );

        private MovieViewFocusContext? CaptureMovieViewFocus()
        {
            if (
                !TryGetCurrentUpperTabContext(out int currentTabIndex, out bool isStandardUpperTab)
                || !isStandardUpperTab
                || !TryGetItemsControlByUpperTabFixedIndex(
                    currentTabIndex,
                    out ItemsControl itemsControl
                )
                || !itemsControl.IsKeyboardFocusWithin
                || Keyboard.FocusedElement is not DependencyObject focusedElement
            )
            {
                return null;
            }

            DependencyObject itemContainer = ItemsControl.ContainerFromElement(
                itemsControl,
                focusedElement
            );
            if (itemContainer == null)
            {
                return null;
            }

            object containerItem = itemsControl.ItemContainerGenerator.ItemFromContainer(
                itemContainer
            );
            MovieRecords focusedMovie = containerItem as MovieRecords;
            if (
                focusedMovie == null
                && itemContainer is FrameworkElement frameworkElement
                && frameworkElement.DataContext is MovieRecords dataContextMovie
            )
            {
                focusedMovie = dataContextMovie;
            }

            if (
                focusedMovie == null
                || !MovieViewFocusAnchorPolicy.TryCapture(focusedMovie, out MovieViewFocusAnchor anchor)
            )
            {
                return null;
            }

            return new MovieViewFocusContext(currentTabIndex, anchor);
        }

        private void RestoreMovieViewFocus(
            MovieViewFocusContext? focusContext,
            FilteredMovieRecsUpdateMode updateMode,
            FilteredMovieRecsUpdateResult collectionResult
        )
        {
            if (
                focusContext is not MovieViewFocusContext captured
                || !IsActive
                || !TryGetCurrentUpperTabContext(out int currentTabIndex, out bool isStandardUpperTab)
                || !isStandardUpperTab
                || currentTabIndex != captured.TabIndex
                || !TryGetItemsControlByUpperTabFixedIndex(
                    currentTabIndex,
                    out ItemsControl itemsControl
                )
            )
            {
                return;
            }

            MovieRecords focusedMovie = MovieViewFocusAnchorPolicy.ResolveAfterCollectionApply(
                captured.Anchor,
                MainVM?.FilteredMovieRecs,
                updateMode,
                collectionResult.HasChanges
            );
            if (
                focusedMovie == null
                || itemsControl.ItemContainerGenerator.ContainerFromItem(focusedMovie)
                    is not UIElement realizedContainer
            )
            {
                return;
            }

            // Reset 後も実現済みの同じ項目だけへ戻し、scroll位置と他ペインのfocusを守る。
            _ = realizedContainer.Focus();
        }
    }
}
