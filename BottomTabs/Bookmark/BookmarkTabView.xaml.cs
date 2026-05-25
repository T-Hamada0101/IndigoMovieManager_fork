using System.Windows.Controls;

namespace IndigoMovieManager.BottomTabs.Bookmark
{
    public partial class BookmarkTabView : UserControl
    {
        public BookmarkTabView()
        {
            InitializeComponent();
        }

        // MainWindow からの見た目更新要求はこの窓口だけへ寄せる。
        public void RefreshItems()
        {
            // BookmarkRecs は ObservableCollection なので Clear/Add の通知で描画は更新される。
            // ここで ListView 全体を Refresh し直す必要はない。
        }
    }
}
