namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker側では追加メタ取得を必須にせず、取れない時は各エンジン側へ委ねる。
    /// </summary>
    internal sealed class WorkerVideoMetadataProvider : IVideoMetadataProvider
    {
        public bool TryGetVideoCodec(string moviePath, out string codec)
        {
            codec = "";
            return false;
        }

        public bool TryGetDurationSec(string moviePath, out double durationSec)
        {
            durationSec = 0;
            return false;
        }
    }
}
