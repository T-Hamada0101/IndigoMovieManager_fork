using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Thumbnail.DropTool
{
    internal sealed class ThumbnailSizeOption
    {
        public ThumbnailSizeOption(int tabIndex, string name, string tag)
        {
            TabIndex = tabIndex;
            Name = name ?? "";
            Tag = tag ?? "";
        }

        public int TabIndex { get; }
        public string Name { get; }
        public string Tag { get; }
        public string DisplayText => $"{Name} ({Tag})";
    }

    internal sealed class ParallelismOption
    {
        public ParallelismOption(int value)
        {
            Value = value < 1 ? 1 : value;
        }

        public int Value { get; }
        public string DisplayText => $"{Value}";
    }

    // 専用ツールでは追加メタ取得を持たず、既存エンジンの通常経路へ委ねる。
    internal sealed class DropToolVideoMetadataProvider : IVideoMetadataProvider
    {
        public bool TryGetDurationSec(string moviePath, out double durationSec)
        {
            durationSec = 0;
            return false;
        }

        public bool TryGetVideoCodec(string moviePath, out string codec)
        {
            codec = "";
            return false;
        }
    }

    // 専用ツールのログは画面下のログ欄へそのまま流して追跡しやすくする。
    internal sealed class DropToolThumbnailLogger : IThumbnailLogger
    {
        private readonly Action<string> appendLog;

        public DropToolThumbnailLogger(Action<string> appendLog)
        {
            this.appendLog = appendLog ?? throw new ArgumentNullException(nameof(appendLog));
        }

        public void LogDebug(string category, string message)
        {
            appendLog($"[debug:{category}] {message}");
        }

        public void LogInfo(string category, string message)
        {
            appendLog($"[info:{category}] {message}");
        }

        public void LogWarning(string category, string message)
        {
            appendLog($"[warn:{category}] {message}");
        }

        public void LogError(string category, string message)
        {
            appendLog($"[error:{category}] {message}");
        }
    }
}
