namespace IndigoMovieManager.Watcher
{
    // Watcher から見た管理者 file index client の最小契約だけを切り出す。
    internal interface IAdminFileIndexClient
    {
        AvailabilityResult CheckAvailability();
        FileIndexMovieResult CollectMoviePaths(FileIndexQueryOptions options);
    }
}
