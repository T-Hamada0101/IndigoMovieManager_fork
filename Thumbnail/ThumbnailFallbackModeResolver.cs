using System.Diagnostics;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// in-process fallback の許可条件を一箇所で決める。
    /// 通常運用は external worker を本線とし、fallback は明示許可時だけ使う。
    /// </summary>
    internal static class ThumbnailFallbackModeResolver
    {
        private const string AllowFallbackEnvName = "IMM_THUMB_ALLOW_INPROCESS_FALLBACK";

        public static ThumbnailFallbackModeDecision Resolve()
        {
            string raw = Environment.GetEnvironmentVariable(AllowFallbackEnvName) ?? "";
            if (TryParseEnabled(raw))
            {
                return new ThumbnailFallbackModeDecision(
                    AllowInProcessFallback: true,
                    Reason: $"env:{AllowFallbackEnvName}={raw}"
                );
            }

            if (Debugger.IsAttached)
            {
                return new ThumbnailFallbackModeDecision(
                    AllowInProcessFallback: true,
                    Reason: "debugger-attached"
                );
            }

            return new ThumbnailFallbackModeDecision(
                AllowInProcessFallback: false,
                Reason: "external-worker-required"
            );
        }

        private static bool TryParseEnabled(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return raw.Trim().ToLowerInvariant() switch
            {
                "1" => true,
                "true" => true,
                "yes" => true,
                "on" => true,
                "enabled" => true,
                _ => false,
            };
        }
    }

    internal readonly record struct ThumbnailFallbackModeDecision(
        bool AllowInProcessFallback,
        string Reason
    );
}
