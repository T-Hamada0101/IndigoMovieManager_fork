namespace IndigoMovieManager
{
    internal static class PlayerVolumePolicy
    {
        internal const double DefaultVolume = 0.25d;

        // 外部通知や保存値が壊れていても、正本へ入れる前に再生可能な範囲へ揃える。
        internal static double Normalize(double volume)
        {
            if (double.IsNaN(volume) || double.IsInfinity(volume))
            {
                return DefaultVolume;
            }

            return Math.Max(0d, Math.Min(1d, volume));
        }

        // 正規値はユーザー設定として尊重し、範囲外または非数値の時だけ保存値を修復する。
        internal static bool RequiresRepair(double volume)
        {
            return double.IsNaN(volume) || double.IsInfinity(volume) || volume < 0d || volume > 1d;
        }
    }
}
