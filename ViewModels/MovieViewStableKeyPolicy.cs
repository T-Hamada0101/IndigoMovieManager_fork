namespace IndigoMovieManager
{
    internal static class MovieViewStableKeyPolicy
    {
        internal static bool TryResolve(MovieRecords movie, out string stableKey)
        {
            stableKey = "";
            if (movie == null)
            {
                return false;
            }

            // DB登録済み行はパス更新後も同じ動画として追えるよう、Movie_Id を優先する。
            if (movie.Movie_Id > 0)
            {
                stableKey = $"id:{movie.Movie_Id}";
                return true;
            }

            string moviePath = movie.Movie_Path ?? "";
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return false;
            }

            stableKey = $"path:{moviePath}";
            return true;
        }

        internal static bool AreSame(MovieRecords left, MovieRecords right)
        {
            return TryResolve(left, out string leftStableKey)
                && TryResolve(right, out string rightStableKey)
                && AreSame(leftStableKey, rightStableKey);
        }

        internal static bool AreSame(string leftStableKey, string rightStableKey)
        {
            return string.Equals(leftStableKey, rightStableKey, StringComparison.OrdinalIgnoreCase);
        }
    }
}
