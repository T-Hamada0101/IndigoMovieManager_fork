using System;
using System.Globalization;

namespace IndigoMovieManager
{
    internal enum PersistenceWriteKind
    {
        ApplicationSettings,
        BackgroundDbWrite,
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
            string logFields =
                $"{request.BuildLogFields()} "
                + $"write_succeeded=false "
                + $"elapsed_ms={elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} "
                + $"failure_kind={ToLogValue(failureKind)} "
                + PersistenceFailureNotificationPolicy.BuildLogFields(failureKind);

            return new(false, elapsed, failureKind, logFields);
        }

        internal static PersistenceWriteResult FromSuccess(
            PersistenceWriteRequest request,
            TimeSpan elapsed
        )
        {
            string logFields =
                $"{request.BuildLogFields()} "
                + $"write_succeeded=true "
                + $"elapsed_ms={elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} "
                + "failure_kind=none";

            return new(true, elapsed, null, logFields);
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
