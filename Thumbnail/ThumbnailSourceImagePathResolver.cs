using System.IO;

namespace IndigoMovieManager;

/// <summary>
/// 動画の隣にある同名画像を、UI fallback や事前判定で安全に拾う。
/// </summary>
internal static class ThumbnailSourceImagePathResolver
{
    private static readonly string[] SupportedImageExtensions = [".jpg", ".jpeg", ".png"];

    internal static bool HasSameNameThumbnailSourceImage(string movieFullPath)
    {
        return TryResolveSameNameThumbnailSourceImagePath(movieFullPath, out _);
    }

    internal static bool TryResolveSameNameThumbnailSourceImagePath(
        string movieFullPath,
        out string sourceImagePath
    )
    {
        sourceImagePath = "";
        if (string.IsNullOrWhiteSpace(movieFullPath))
        {
            return false;
        }

        for (int i = 0; i < SupportedImageExtensions.Length; i++)
        {
            string candidatePath = Path.ChangeExtension(
                movieFullPath,
                SupportedImageExtensions[i]
            );
            if (!HasUsableFile(candidatePath))
            {
                continue;
            }

            sourceImagePath = candidatePath;
            return true;
        }

        return false;
    }

    // UI fallback では「存在して開ける可能性が高い画像」だけを返したいので 0 byte は除外する。
    private static bool HasUsableFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            FileInfo fi = new(path);
            return fi.Exists && fi.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 1件の動画レコード内で、同名source imageの探索結果を遅延共有する。
/// </summary>
internal sealed class LazyThumbnailSourceImagePathResolver
{
    private readonly string _movieFullPath;
    private bool _resolved;
    private string _sourceImagePath = "";

    internal LazyThumbnailSourceImagePathResolver(string movieFullPath)
    {
        _movieFullPath = movieFullPath;
    }

    internal int ProbeCount { get; private set; }

    internal int CacheHitCount { get; private set; }

    internal string Resolve()
    {
        if (_resolved)
        {
            CacheHitCount++;
            return _sourceImagePath;
        }

        // 最初に管理サムネが欠損した用途だけが、実ファイル探索を引き受ける。
        _resolved = true;
        ProbeCount++;
        ThumbnailSourceImagePathResolver.TryResolveSameNameThumbnailSourceImagePath(
            _movieFullPath,
            out _sourceImagePath
        );
        return _sourceImagePath;
    }
}
