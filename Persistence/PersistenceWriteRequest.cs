using System;
using System.Globalization;

namespace IndigoMovieManager
{
    internal enum PersistenceWriteKind
    {
        ApplicationSettings,
        BackgroundDbWrite,
        CurrentDbSettings,
    }

    internal readonly record struct PersistenceWriteRequest(
        PersistenceWriteKind Kind,
        string Reason,
        string QueueKey,
        bool Retryable
    )
    {
        internal static PersistenceWriteRequest Create(
            PersistenceWriteKind kind,
            string reason,
            string queueKey,
            bool retryable
        )
        {
            return new(
                kind,
                NormalizeLogValue(reason),
                NormalizeLogValue(queueKey),
                retryable
            );
        }

        internal string BuildLogFields()
        {
            return $"write_kind={ToLogValue(Kind)} "
                + "persist_contract=persistence-write-v1 "
                + $"write_reason={Reason} "
                + $"queue_key={QueueKey} "
                + $"retryable_policy={ToLogBool(Retryable)}";
        }

        internal string BuildWriteSuccessResultLogFields(TimeSpan elapsed)
        {
            return PersistenceWriteResult.FromSuccess(this, elapsed).LogFields;
        }

        private static string NormalizeLogValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        }

        private static string ToLogValue(PersistenceWriteKind kind)
        {
            return kind switch
            {
                PersistenceWriteKind.ApplicationSettings => "application-settings",
                PersistenceWriteKind.BackgroundDbWrite => "background-db-write",
                PersistenceWriteKind.CurrentDbSettings => "current-db-settings",
                _ => kind.ToString().ToLowerInvariant(),
            };
        }

        private static string ToLogBool(bool value)
        {
            return value ? "true" : "false";
        }
    }

    internal readonly record struct PersistenceWriteResult(
        bool Succeeded,
        TimeSpan Elapsed,
        PersistenceFailureKind? FailureKind,
        string LogFields
    )
    {
        internal static PersistenceWriteResult FromFailure(
            PersistenceWriteRequest request,
            TimeSpan elapsed,
            PersistenceFailureKind failureKind
        )
        {
            PersistenceFailureNotificationState notificationState =
                PersistenceFailureNotificationPolicy.BuildFailureState(failureKind);
            string logFields =
                $"{request.BuildLogFields()} "
                + $"write_succeeded=false "
                + $"elapsed_ms={elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} "
                + $"failure_kind={ToLogValue(failureKind)} "
                + $"persist_state={ToPersistStateLogValue(notificationState)} "
                + PersistenceFailureNotificationPolicy.BuildLogFields(notificationState);

            return new(false, elapsed, failureKind, logFields);
        }

        internal static PersistenceWriteResult FromSuccess(
            PersistenceWriteRequest request,
            TimeSpan elapsed
        )
        {
            PersistenceFailureNotificationState notificationState =
                PersistenceFailureNotificationPolicy.BuildSuccessState();
            string logFields =
                $"{request.BuildLogFields()} "
                + $"write_succeeded=true "
                + $"elapsed_ms={elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} "
                + "failure_kind=none "
                + $"persist_state={ToPersistStateLogValue(notificationState)} "
                + PersistenceFailureNotificationPolicy.BuildLogFields(notificationState);

            return new(true, elapsed, null, logFields);
        }

        private static string ToPersistStateLogValue(PersistenceFailureNotificationState state)
        {
            if (!state.Dirty && !state.Failed)
            {
                return "persisted";
            }

            if (state.Dirty && state.Retryable)
            {
                return "dirty-retryable";
            }

            if (state.Failed && state.NotifyUi)
            {
                return "failed-notify";
            }

            return state.Failed ? "failed" : "dirty";
        }

        private static string ToLogValue(PersistenceFailureKind kind)
        {
            return kind switch
            {
                PersistenceFailureKind.BackgroundDbWrite => "background-db-write",
                PersistenceFailureKind.ApplicationSettings => "application-settings",
                PersistenceFailureKind.Bookmark => "bookmark",
                PersistenceFailureKind.SkinProfile => "skin-profile",
                PersistenceFailureKind.SkinSystem => "skin-system",
                _ => kind.ToString().ToLowerInvariant(),
            };
        }
    }
}
