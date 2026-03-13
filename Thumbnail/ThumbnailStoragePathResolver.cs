using System;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    internal static class ThumbnailStoragePathResolver
    {
        internal static string ResolveThumbFolder(string dbName, string thumbFolder)
        {
            if (!string.IsNullOrWhiteSpace(thumbFolder))
            {
                return thumbFolder;
            }

            // 既定保存先は作業ディレクトリではなく、実行ファイル配置先に固定する。
            return Path.Combine(GetExecutableDirectory(), "Thumb", dbName ?? "");
        }

        internal static string GetExecutableDirectory()
        {
            return Path.GetFullPath(AppContext.BaseDirectory);
        }
    }
}
