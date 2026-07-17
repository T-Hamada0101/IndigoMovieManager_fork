using System.IO;

namespace IndigoMovieManager.BottomTabs.FileOrganizer
{
    internal static class FileOrganizerMoveConfirmationPolicy
    {
        // 通常移動と全移動で同じ確認文を使い、代表ファイル名と実件数を必ず見せる。
        internal static string BuildMessage(
            string representativeMoviePath,
            int targetCount,
            string destinationFolder
        )
        {
            string representativeName = Path.GetFileName(representativeMoviePath ?? "");
            int normalizedCount = Math.Max(0, targetCount);
            return $"次の動画を移動しますか？\n\n代表ファイル: {representativeName}\n移動件数: {normalizedCount} 件\n移動先: {destinationFolder ?? ""}";
        }
    }
}
