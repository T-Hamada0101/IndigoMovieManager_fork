namespace IndigoMovieManager
{
    internal enum PersistenceFailureKind
    {
        BackgroundDbWrite,
        ApplicationSettings,
        Bookmark,
        SkinProfile,
        SkinSystem,
    }

    internal readonly record struct PersistenceFailureNotificationState(
        bool Dirty,
        bool Failed,
        bool Retryable,
        bool NotifyUi
    );

    /// <summary>
    /// 保存失敗時の軽量状態と通知条件を、実際の保存処理から切り離してそろえる。
    /// </summary>
    internal static class PersistenceFailureNotificationPolicy
    {
        internal static PersistenceFailureNotificationState BuildFailureState(
            PersistenceFailureKind kind
        )
        {
            return kind switch
            {
                PersistenceFailureKind.SkinSystem => new(
                    Dirty: false,
                    Failed: true,
                    Retryable: false,
                    NotifyUi: true
                ),
                _ => new(
                    Dirty: true,
                    Failed: true,
                    Retryable: true,
                    NotifyUi: false
                ),
            };
        }

        internal static PersistenceFailureNotificationState BuildSuccessState()
        {
            return new(Dirty: false, Failed: false, Retryable: false, NotifyUi: false);
        }

        internal static string BuildLogFields(PersistenceFailureKind kind)
        {
            return BuildLogFields(BuildFailureState(kind));
        }

        internal static string BuildLogFields(PersistenceFailureNotificationState state)
        {
            return $"dirty={ToLogBool(state.Dirty)} "
                + $"failed={ToLogBool(state.Failed)} "
                + $"retryable={ToLogBool(state.Retryable)} "
                + $"notify_ui={ToLogBool(state.NotifyUi)}";
        }

        private static string ToLogBool(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
