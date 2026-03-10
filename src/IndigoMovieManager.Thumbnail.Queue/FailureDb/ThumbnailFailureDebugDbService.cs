using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace IndigoMovieManager.Thumbnail.FailureDb
{
    // サムネ失敗専用DBの初期化・追記・取得をまとめる。
    public sealed class ThumbnailFailureDebugDbService
    {
        private const string UtcDateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        private readonly object initializeLock = new();
        private readonly string mainDbFullPath;
        private readonly string dbName;
        private readonly string failureDbFullPath;
        private readonly string mainDbPathHash;
        private bool isInitialized;

        public ThumbnailFailureDebugDbService(string mainDbFullPath)
        {
            if (string.IsNullOrWhiteSpace(mainDbFullPath))
            {
                throw new ArgumentException("mainDbFullPath is required.", nameof(mainDbFullPath));
            }

            this.mainDbFullPath = mainDbFullPath;
            dbName = Path.GetFileNameWithoutExtension(mainDbFullPath) ?? "";
            failureDbFullPath = ThumbnailFailureDebugDbPathResolver.ResolveFailureDbPath(mainDbFullPath);
            mainDbPathHash = QueueDb.QueueDbPathResolver.GetMainDbPathHash8(mainDbFullPath);
        }

        public string MainDbFullPath => mainDbFullPath;
        public string DbName => dbName;
        public string FailureDbFullPath => failureDbFullPath;
        public string MainDbPathHash => mainDbPathHash;

        public void EnsureInitialized()
        {
            if (isInitialized)
            {
                return;
            }

            lock (initializeLock)
            {
                if (isInitialized)
                {
                    return;
                }

                string directory = Path.GetDirectoryName(failureDbFullPath) ?? "";
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using SQLiteConnection connection = CreateConnection();
                connection.Open();
                ThumbnailFailureDebugDbSchema.EnsureCreated(connection);
                isInitialized = true;
            }
        }

        public long InsertFailureRecord(ThumbnailFailureRecord record)
        {
            EnsureInitialized();
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO ThumbnailFailureDebug (
    DbName,
    MainDbFullPath,
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    PanelType,
    MovieSizeBytes,
    Duration,
    Reason,
    FailureKind,
    AttemptCount,
    OccurredAtUtc,
    UpdatedAtUtc,
    TabIndex,
    OwnerInstanceId,
    WorkerRole,
    EngineId,
    QueueStatus,
    LeaseUntilUtc,
    StartedAtUtc,
    LastError,
    ExtraJson
) VALUES (
    @DbName,
    @MainDbFullPath,
    @MainDbPathHash,
    @MoviePath,
    @MoviePathKey,
    @PanelType,
    @MovieSizeBytes,
    @Duration,
    @Reason,
    @FailureKind,
    @AttemptCount,
    @OccurredAtUtc,
    @UpdatedAtUtc,
    @TabIndex,
    @OwnerInstanceId,
    @WorkerRole,
    @EngineId,
    @QueueStatus,
    @LeaseUntilUtc,
    @StartedAtUtc,
    @LastError,
    @ExtraJson
);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("@DbName", ResolveDbName(record));
            command.Parameters.AddWithValue("@MainDbFullPath", ResolveMainDbFullPath(record));
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@MoviePath", record.MoviePath ?? "");
            command.Parameters.AddWithValue(
                "@MoviePathKey",
                ThumbnailFailureDebugDbPathResolver.CreateMoviePathKey(record.MoviePath)
            );
            command.Parameters.AddWithValue("@PanelType", record.PanelType ?? "");
            command.Parameters.AddWithValue("@MovieSizeBytes", Math.Max(0, record.MovieSizeBytes));
            command.Parameters.AddWithValue(
                "@Duration",
                record.Duration.HasValue ? record.Duration.Value : (object)DBNull.Value
            );
            command.Parameters.AddWithValue("@Reason", record.Reason ?? "");
            command.Parameters.AddWithValue("@FailureKind", record.FailureKind.ToString());
            command.Parameters.AddWithValue("@AttemptCount", Math.Max(0, record.AttemptCount));
            command.Parameters.AddWithValue("@OccurredAtUtc", ToUtcText(record.OccurredAtUtc));
            command.Parameters.AddWithValue("@UpdatedAtUtc", ToUtcText(record.UpdatedAtUtc));
            command.Parameters.AddWithValue(
                "@TabIndex",
                record.TabIndex.HasValue ? record.TabIndex.Value : (object)DBNull.Value
            );
            command.Parameters.AddWithValue("@OwnerInstanceId", record.OwnerInstanceId ?? "");
            command.Parameters.AddWithValue("@WorkerRole", record.WorkerRole ?? "");
            command.Parameters.AddWithValue("@EngineId", record.EngineId ?? "");
            command.Parameters.AddWithValue("@QueueStatus", record.QueueStatus ?? "");
            command.Parameters.AddWithValue("@LeaseUntilUtc", record.LeaseUntilUtc ?? "");
            command.Parameters.AddWithValue("@StartedAtUtc", record.StartedAtUtc ?? "");
            command.Parameters.AddWithValue("@LastError", record.LastError ?? "");
            command.Parameters.AddWithValue("@ExtraJson", record.ExtraJson ?? "");
            object insertedId = command.ExecuteScalar();
            return Convert.ToInt64(insertedId, CultureInfo.InvariantCulture);
        }

        public List<ThumbnailFailureRecord> GetFailureRecords()
        {
            EnsureInitialized();

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    RecordId,
    DbName,
    MainDbFullPath,
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    PanelType,
    MovieSizeBytes,
    Duration,
    Reason,
    FailureKind,
    AttemptCount,
    OccurredAtUtc,
    UpdatedAtUtc,
    TabIndex,
    OwnerInstanceId,
    WorkerRole,
    EngineId,
    QueueStatus,
    LeaseUntilUtc,
    StartedAtUtc,
    LastError,
    ExtraJson
FROM ThumbnailFailureDebug
WHERE MainDbPathHash = @MainDbPathHash
ORDER BY OccurredAtUtc DESC, RecordId DESC;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);

            List<ThumbnailFailureRecord> records = [];
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                ThumbnailFailureRecord record = new()
                {
                    RecordId = reader.GetInt64(0),
                    DbName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    MainDbFullPath = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    MainDbPathHash = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    MoviePath = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    MoviePathKey = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    PanelType = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    MovieSizeBytes = reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                    Duration = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    Reason = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    FailureKind = ParseFailureKind(reader.IsDBNull(10) ? "" : reader.GetString(10)),
                    AttemptCount = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                    OccurredAtUtc = ParseUtcText(reader.IsDBNull(12) ? "" : reader.GetString(12)),
                    UpdatedAtUtc = ParseUtcText(reader.IsDBNull(13) ? "" : reader.GetString(13)),
                    TabIndex = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                    OwnerInstanceId = reader.IsDBNull(15) ? "" : reader.GetString(15),
                    WorkerRole = reader.IsDBNull(16) ? "" : reader.GetString(16),
                    EngineId = reader.IsDBNull(17) ? "" : reader.GetString(17),
                    QueueStatus = reader.IsDBNull(18) ? "" : reader.GetString(18),
                    LeaseUntilUtc = reader.IsDBNull(19) ? "" : reader.GetString(19),
                    StartedAtUtc = reader.IsDBNull(20) ? "" : reader.GetString(20),
                    LastError = reader.IsDBNull(21) ? "" : reader.GetString(21),
                    ExtraJson = reader.IsDBNull(22) ? "" : reader.GetString(22),
                };
                records.Add(record);
            }

            return records;
        }

        private SQLiteConnection OpenConnection()
        {
            SQLiteConnection connection = CreateConnection();
            connection.Open();
            ThumbnailFailureDebugDbSchema.ApplyConnectionPragmas(connection);
            return connection;
        }

        private SQLiteConnection CreateConnection()
        {
            return new SQLiteConnection($"Data Source={failureDbFullPath}");
        }

        private string ResolveDbName(ThumbnailFailureRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.DbName))
            {
                return record.DbName;
            }

            if (!string.IsNullOrWhiteSpace(dbName))
            {
                return dbName;
            }

            return "main";
        }

        private string ResolveMainDbFullPath(ThumbnailFailureRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.MainDbFullPath))
            {
                return record.MainDbFullPath;
            }

            return mainDbFullPath;
        }

        private static ThumbnailFailureKind ParseFailureKind(string raw)
        {
            return Enum.TryParse(raw ?? "", ignoreCase: true, out ThumbnailFailureKind parsed)
                ? parsed
                : ThumbnailFailureKind.Unknown;
        }

        private static string ToUtcText(DateTime dateTime)
        {
            return dateTime.ToUniversalTime().ToString(UtcDateFormat, CultureInfo.InvariantCulture);
        }

        private static DateTime ParseUtcText(string text)
        {
            if (
                DateTime.TryParseExact(
                    text ?? "",
                    UtcDateFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime parsed
                )
            )
            {
                return parsed;
            }

            return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        }
    }
}
