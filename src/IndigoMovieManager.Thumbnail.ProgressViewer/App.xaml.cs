using System.Diagnostics;
using System.Windows;

namespace IndigoMovieManager
{
    public partial class ThumbnailProgressViewerApp : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ThumbnailProgressViewerRuntimeOptions options = ThumbnailProgressViewerRuntimeOptions.Parse(
                e.Args ?? []
            );
            ThumbnailProgressViewerWindow window = new(options);
            MainWindow = window;
            window.Show();
        }
    }

    public sealed class ThumbnailProgressViewerRuntimeOptions
    {
        public string MainDbFullPath { get; init; } = "";
        public string DbName { get; init; } = "";
        public string NormalOwnerInstanceId { get; init; } = "";
        public string IdleOwnerInstanceId { get; init; } = "";
        public string CoordinatorOwnerInstanceId { get; init; } = "";
        public int ParentProcessId { get; init; }

        public static ThumbnailProgressViewerRuntimeOptions Parse(string[] args)
        {
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string current = args[i] ?? "";
                if (!current.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                string key = current[2..];
                string value = "";
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i] ?? "";
                }

                values[key] = value;
            }

            return new ThumbnailProgressViewerRuntimeOptions
            {
                MainDbFullPath = GetOptional(values, "main-db", ""),
                DbName = GetOptional(values, "db-name", ""),
                NormalOwnerInstanceId = GetOptional(values, "normal-owner", ""),
                IdleOwnerInstanceId = GetOptional(values, "idle-owner", ""),
                CoordinatorOwnerInstanceId = GetOptional(values, "coordinator-owner", ""),
                ParentProcessId = ParseInt(GetOptional(values, "parent-pid", "0"), 0),
            };
        }

        private static string GetOptional(
            IReadOnlyDictionary<string, string> values,
            string key,
            string defaultValue
        )
        {
            return values.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : defaultValue;
        }

        private static int ParseInt(string raw, int defaultValue)
        {
            return int.TryParse(raw, out int parsed) ? parsed : defaultValue;
        }
    }
}
