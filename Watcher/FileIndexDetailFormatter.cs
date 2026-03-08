namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// Provider詳細コードを、ログとUI通知で同じ文言へ寄せる。
    /// </summary>
    internal static class FileIndexDetailFormatter
    {
        public static (string Code, string Message) Describe(
            string providerDisplayName,
            string detail
        )
        {
            string safeProviderName = string.IsNullOrWhiteSpace(providerDisplayName)
                ? "インデックス連携"
                : providerDisplayName;
            string safeDetail = string.IsNullOrWhiteSpace(detail) ? "unknown" : detail;
            if (
                safeDetail.StartsWith(
                    EverythingReasonCodes.PathNotEligiblePrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                string rawReason = safeDetail[EverythingReasonCodes.PathNotEligiblePrefix.Length..];
                string message = rawReason switch
                {
                    "empty_path" => "監視フォルダが未設定です",
                    "unc_path" => "UNC/NASパスはEverything高速経路の対象外です",
                    "no_root" => "ドライブ情報を解決できません",
                    "ok" => "対象フォルダ判定は正常です",
                    _ when rawReason.StartsWith(
                            "drive_type_",
                            StringComparison.OrdinalIgnoreCase
                        ) => $"ローカル固定ドライブ以外のため対象外です ({rawReason})",
                    _ when rawReason.StartsWith(
                            "drive_format_",
                            StringComparison.OrdinalIgnoreCase
                        ) => $"NTFS以外のため対象外です ({rawReason})",
                    _ when rawReason.StartsWith(
                            "eligibility_error:",
                            StringComparison.OrdinalIgnoreCase
                        ) => $"対象判定で例外が発生しました ({rawReason})",
                    _ => $"対象外です ({rawReason})",
                };
                return (safeDetail, message);
            }

            if (
                safeDetail.Equals(
                    EverythingReasonCodes.SettingDisabled,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (safeDetail, "設定でインデックス連携が無効です");
            }

            if (
                safeDetail.Equals(
                    EverythingReasonCodes.EverythingNotAvailable,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (safeDetail, $"{safeProviderName} を利用できません");
            }

            if (
                safeDetail.Equals(
                    EverythingReasonCodes.AutoNotAvailable,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (
                    safeDetail,
                    $"AUTO設定中ですが {safeProviderName} を使えないため通常監視で動作します"
                );
            }

            if (
                safeDetail.StartsWith(
                    EverythingReasonCodes.EverythingResultTruncatedPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return (
                    safeDetail,
                    $"{safeProviderName} の検索結果が上限件数に達したため通常監視へ切り替えます"
                );
            }

            if (
                safeDetail.StartsWith(
                    EverythingReasonCodes.AvailabilityErrorPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                string errorName = safeDetail[EverythingReasonCodes.AvailabilityErrorPrefix.Length..];
                return (safeDetail, DescribeAvailabilityError(safeProviderName, errorName));
            }

            if (
                safeDetail.StartsWith(
                    EverythingReasonCodes.EverythingQueryErrorPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                string errorName = safeDetail[
                    EverythingReasonCodes.EverythingQueryErrorPrefix.Length..
                ];
                return (safeDetail, DescribeQueryError(safeProviderName, errorName));
            }

            if (
                safeDetail.StartsWith(
                    EverythingReasonCodes.EverythingThumbQueryErrorPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                string errorName = safeDetail[
                    EverythingReasonCodes.EverythingThumbQueryErrorPrefix.Length..
                ];
                return (
                    safeDetail,
                    $"{safeProviderName} のサムネ参照中に例外が発生しました ({errorName})"
                );
            }

            if (
                safeDetail.StartsWith(
                    EverythingReasonCodes.OkPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                if (
                    safeDetail.Equals(
                        EverythingReasonCodes.EmptyResultFallback,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return (
                        safeDetail,
                        $"{safeProviderName} が0件を返したため通常監視で再走査しました"
                    );
                }

                return (safeDetail, $"{safeProviderName} で候補収集に成功しました");
            }

            return (safeDetail, $"不明な理由のため通常監視へ切り替えます ({safeDetail})");
        }

        private static string DescribeAvailabilityError(string providerDisplayName, string errorName)
        {
            if (string.Equals(errorName, "AdminRequired", StringComparison.OrdinalIgnoreCase))
            {
                return $"{providerDisplayName} は管理者権限が必要です";
            }

            if (
                string.Equals(
                    errorName,
                    "AdminServiceUnavailable",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return $"{providerDisplayName} の管理者サービスへ接続できないため通常監視で継続します";
            }

            if (string.Equals(errorName, "TimeoutException", StringComparison.OrdinalIgnoreCase))
            {
                return $"{providerDisplayName} の可用性確認がタイムアウトしたため通常監視で継続します";
            }

            if (
                string.Equals(
                    errorName,
                    "UnauthorizedAccessException",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return $"{providerDisplayName} の可用性確認で権限不足が発生しました";
            }

            return $"{providerDisplayName} の利用可否確認で例外が発生しました ({errorName})";
        }

        private static string DescribeQueryError(string providerDisplayName, string errorName)
        {
            if (string.Equals(errorName, "TimeoutException", StringComparison.OrdinalIgnoreCase))
            {
                return $"{providerDisplayName} の応答がタイムアウトしたため通常監視へ切り替えます";
            }

            if (
                string.Equals(
                    errorName,
                    "UnauthorizedAccessException",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return $"{providerDisplayName} の問い合わせで権限不足が発生したため通常監視へ切り替えます";
            }

            if (
                string.Equals(errorName, "ArgumentException", StringComparison.OrdinalIgnoreCase)
            )
            {
                return $"{providerDisplayName} への問い合わせ条件が不正だったため通常監視へ切り替えます";
            }

            return $"{providerDisplayName} の問い合わせ中に例外が発生しました ({errorName})";
        }
    }
}
