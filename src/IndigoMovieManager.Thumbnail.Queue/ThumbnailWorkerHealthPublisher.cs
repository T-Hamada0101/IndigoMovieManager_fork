namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker 単位の health 更新を簡単に行う薄い publisher。
    /// </summary>
    public sealed class ThumbnailWorkerHealthPublisher
    {
        private readonly string mainDbFullPath;
        private readonly string ownerInstanceId;
        private readonly ThumbnailQueueWorkerRole workerRole;
        private readonly string settingsVersionToken;

        public ThumbnailWorkerHealthPublisher(
            string mainDbFullPath,
            string ownerInstanceId,
            ThumbnailQueueWorkerRole workerRole,
            string settingsVersionToken
        )
        {
            this.mainDbFullPath = mainDbFullPath ?? "";
            this.ownerInstanceId = ownerInstanceId ?? "";
            this.workerRole = workerRole;
            this.settingsVersionToken = settingsVersionToken ?? "";
        }

        public void Publish(
            string state,
            int processId,
            string currentPriority,
            string message = "",
            string reasonCode = "",
            int? exitCode = null,
            DateTime? lastHeartbeatUtc = null
        )
        {
            ThumbnailWorkerHealthStore.Save(
                new ThumbnailWorkerHealthSnapshot
                {
                    MainDbFullPath = mainDbFullPath,
                    OwnerInstanceId = ownerInstanceId,
                    WorkerRole = workerRole.ToString(),
                    State = state ?? "",
                    ReasonCode = reasonCode ?? "",
                    SettingsVersionToken = settingsVersionToken,
                    CurrentPriority = currentPriority ?? "",
                    Message = message ?? "",
                    ProcessId = processId,
                    ExitCode = exitCode,
                    UpdatedAtUtc = DateTime.UtcNow,
                    LastHeartbeatUtc = lastHeartbeatUtc,
                }
            );
        }
    }
}
