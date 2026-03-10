using System.Data.SQLite;

namespace IndigoMovieManager.Thumbnail.FailureDb
{
    // 失敗履歴DBのスキーマとPRAGMAをここに集約する。
    public static class ThumbnailFailureDebugDbSchema
    {
        private const string CreateTableSql = @"
CREATE TABLE IF NOT EXISTS ThumbnailFailureDebug (
    RecordId INTEGER PRIMARY KEY AUTOINCREMENT,
    DbName TEXT NOT NULL DEFAULT '',
    MainDbFullPath TEXT NOT NULL DEFAULT '',
    MainDbPathHash TEXT NOT NULL DEFAULT '',
    MoviePath TEXT NOT NULL DEFAULT '',
    MoviePathKey TEXT NOT NULL DEFAULT '',
    PanelType TEXT NOT NULL DEFAULT '',
    MovieSizeBytes INTEGER NOT NULL DEFAULT 0,
    Duration REAL,
    Reason TEXT NOT NULL DEFAULT '',
    FailureKind TEXT NOT NULL DEFAULT 'Unknown',
    AttemptCount INTEGER NOT NULL DEFAULT 0,
    OccurredAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UpdatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    TabIndex INTEGER,
    OwnerInstanceId TEXT NOT NULL DEFAULT '',
    WorkerRole TEXT NOT NULL DEFAULT '',
    EngineId TEXT NOT NULL DEFAULT '',
    QueueStatus TEXT NOT NULL DEFAULT '',
    LeaseUntilUtc TEXT NOT NULL DEFAULT '',
    StartedAtUtc TEXT NOT NULL DEFAULT '',
    LastError TEXT NOT NULL DEFAULT '',
    ExtraJson TEXT NOT NULL DEFAULT ''
);";

        private const string CreateIndexMainDbSql = @"
CREATE INDEX IF NOT EXISTS IX_ThumbnailFailureDebug_MainDb_Occurred
ON ThumbnailFailureDebug (MainDbPathHash, OccurredAtUtc DESC, RecordId DESC);";

        private const string CreateIndexMovieSql = @"
CREATE INDEX IF NOT EXISTS IX_ThumbnailFailureDebug_Movie
ON ThumbnailFailureDebug (MainDbPathHash, MoviePathKey, OccurredAtUtc DESC);";

        public static void EnsureCreated(SQLiteConnection connection)
        {
            ApplyConnectionPragmas(connection);
            QueueDb.QueueDbSchema.ApplyPragmas(connection);
            ExecuteNonQuery(connection, CreateTableSql);
            ExecuteNonQuery(connection, CreateIndexMainDbSql);
            ExecuteNonQuery(connection, CreateIndexMovieSql);
        }

        public static void ApplyConnectionPragmas(SQLiteConnection connection)
        {
            ExecuteNonQuery(connection, "PRAGMA busy_timeout=5000;");
            ExecuteNonQuery(connection, "PRAGMA synchronous=NORMAL;");
        }

        private static void ExecuteNonQuery(SQLiteConnection connection, string sql)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
