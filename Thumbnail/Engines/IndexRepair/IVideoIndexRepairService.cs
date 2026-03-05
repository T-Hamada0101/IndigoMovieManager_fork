namespace IndigoMovieManager.Thumbnail.Engines.IndexRepair
{
    /// <summary>
    /// インデックス破損判定/修復の抽象。
    /// </summary>
    internal interface IVideoIndexRepairService
    {
        Task<VideoIndexProbeResult> ProbeAsync(
            string moviePath,
            CancellationToken cts = default
        );

        Task<VideoIndexRepairResult> RepairAsync(
            string moviePath,
            string outputPath,
            CancellationToken cts = default
        );
    }
}
