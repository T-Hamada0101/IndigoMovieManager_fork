using System.Text;
using IndigoMovieManager.Thumbnail.Engines;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.Swf;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【サムネイル生成の絶対的オーケストレータ】✨
    /// 状況とルールを見極め、最適な生成エンジンを召喚してサムネイルを爆誕させるぜ！🔥
    /// </summary>
    public sealed class ThumbnailCreationService
    {
        // .NET では既定で一部コードページ（例: 932）が無効なため、
        // 既存処理互換としてCodePagesプロバイダを有効化しておく。
        static ThumbnailCreationService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private readonly ThumbnailCreationRuntime creationRuntime;

        internal ThumbnailCreationService(ThumbnailCreationRuntimeProvider runtimeProvider)
        {
            creationRuntime =
                runtimeProvider?.GetRuntime()
                ?? throw new ArgumentNullException(nameof(runtimeProvider));
        }

        public ThumbnailCreationService()
            : this(
                new ThumbnailCreationRuntimeProvider(
                    NoOpVideoMetadataProvider.Instance,
                    NoOpThumbnailLogger.Instance
                )
            ) { }

        public ThumbnailCreationService(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger
        )
            : this(
                new ThumbnailCreationRuntimeProvider(
                    videoMetadataProvider,
                    logger,
                    new VideoIndexRepairService(),
                    null
                )
            ) { }

        internal ThumbnailCreationService(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IVideoIndexRepairService videoIndexRepairService = null,
            SwfThumbnailGenerationService swfThumbnailGenerationService = null
        )
            : this(
                new ThumbnailCreationRuntimeProvider(
                    videoMetadataProvider,
                    logger,
                    videoIndexRepairService,
                    swfThumbnailGenerationService
                )
            ) { }

        /// <summary>
        /// ブックマーク用のとっておきの一枚（単一フレーム）を生成する専用ルートだ！📸
        /// </summary>
        public async Task<bool> CreateBookmarkThumbAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos
        )
        {
            return await creationRuntime.CreateBookmarkThumbAsync(
                movieFullPath,
                saveThumbPath,
                capturePos,
                CancellationToken.None
            );
        }

        /// <summary>
        /// 動画インデックス破損の疑いを調べる（将来UIからの直接呼び出しにも使う）。
        /// </summary>
        public Task<VideoIndexProbeResult> ProbeVideoIndexAsync(
            string moviePath,
            CancellationToken cts = default
        )
        {
            return creationRuntime.ProbeVideoIndexAsync(moviePath, cts);
        }

        /// <summary>
        /// 動画インデックス修復を実行する（将来UIからの直接呼び出しにも使う）。
        /// </summary>
        public Task<VideoIndexRepairResult> RepairVideoIndexAsync(
            string moviePath,
            string outputPath,
            CancellationToken cts = default
        )
        {
            return creationRuntime.RepairVideoIndexAsync(moviePath, outputPath, cts);
        }

        /// <summary>
        /// サムネイル生成の本丸！通常・手動を問わず、すべての生成処理はここから始まる激アツなメイン・エントリーポイントだぜ！🚀
        /// </summary>
        public async Task<ThumbnailCreateResult> CreateThumbAsync(
            QueueObj queueObj,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual = false,
            CancellationToken cts = default
        )
        {
            return await creationRuntime.CreateThumbAsync(
                queueObj,
                dbName,
                thumbFolder,
                isResizeThumb,
                isManual,
                cts
            );
        }

    }

    /// <summary>
    /// MainWindowへ凱旋報告するための、サムネイル生成結果をまとめたイケてるクラスだ！🏅
    /// </summary>
    public sealed class ThumbnailCreateResult
    {
        public string SaveThumbFileName { get; init; } = "";
        public double? DurationSec { get; init; }
        public bool IsSuccess { get; init; }
        public string ErrorMessage { get; init; } = "";
        public ThumbnailPreviewFrame PreviewFrame { get; init; }
    }

    /// <summary>
    /// WPF非依存でプレビュー画素を受け渡すための中立DTO。
    /// </summary>
    public sealed class ThumbnailPreviewFrame
    {
        public byte[] PixelBytes { get; init; } = [];
        public int Width { get; init; }
        public int Height { get; init; }
        public int Stride { get; init; }
        public ThumbnailPreviewPixelFormat PixelFormat { get; init; } =
            ThumbnailPreviewPixelFormat.Bgr24;

        public bool IsValid()
        {
            if (PixelBytes == null || Width < 1 || Height < 1 || Stride < 1)
            {
                return false;
            }

            long requiredLength = (long)Stride * Height;
            if (requiredLength < 1 || requiredLength > int.MaxValue)
            {
                return false;
            }

            return PixelBytes.Length >= requiredLength;
        }
    }

    public enum ThumbnailPreviewPixelFormat
    {
        Unknown = 0,
        Bgr24 = 1,
        Bgra32 = 2,
    }

}
