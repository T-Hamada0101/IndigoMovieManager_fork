namespace IndigoMovieManager.Thumbnail
{
    internal static class WorkerStartupModeResolver
    {
        public static bool ShouldRunDropUi(string[] args)
        {
            if (args == null || args.Length < 1)
            {
                return true;
            }

            return !HasWorkerRuntimeArguments(args);
        }

        // Worker本線の必須引数が見えている時は、drop-manifest 混在より本線継続を優先する。
        internal static bool HasWorkerRuntimeArguments(string[] args)
        {
            if (args == null || args.Length < 1)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string current = args[i] ?? "";
                if (!current.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                if (
                    string.Equals(current, "--role", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(current, "--main-db", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(current, "--owner", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        current,
                        "--settings-snapshot",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
}
