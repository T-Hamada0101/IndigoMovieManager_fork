using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// エンジンIDと実体の対応を一箇所へ寄せる小さなカタログ。
    /// service 本体は順序ポリシーと実体解決をここへ委譲する。
    /// </summary>
    internal sealed class ThumbnailEngineCatalog
    {
        private readonly IThumbnailGenerationEngine ffMediaToolkitEngine;
        private readonly IThumbnailGenerationEngine ffmpegOnePassEngine;
        private readonly IThumbnailGenerationEngine openCvEngine;
        private readonly IThumbnailGenerationEngine autogenEngine;

        public ThumbnailEngineCatalog(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine
        )
        {
            this.ffMediaToolkitEngine =
                ffMediaToolkitEngine
                ?? throw new ArgumentNullException(nameof(ffMediaToolkitEngine));
            this.ffmpegOnePassEngine =
                ffmpegOnePassEngine ?? throw new ArgumentNullException(nameof(ffmpegOnePassEngine));
            this.openCvEngine =
                openCvEngine ?? throw new ArgumentNullException(nameof(openCvEngine));
            this.autogenEngine =
                autogenEngine ?? throw new ArgumentNullException(nameof(autogenEngine));
        }

        // 実行順は policy に従い、ここでは engine id を実体へ戻すだけに徹する。
        public List<IThumbnailGenerationEngine> BuildExecutionOrder(
            IThumbnailGenerationEngine selectedEngine,
            ThumbnailJobContext context
        )
        {
            List<IThumbnailGenerationEngine> order = [];
            List<string> orderIds = ThumbnailExecutionPolicy.BuildEngineOrderIds(
                selectedEngine?.EngineId ?? "",
                isManual: context?.IsManual == true,
                hasEmojiPath: context?.HasEmojiPath == true,
                attemptCount: context?.QueueObj?.AttemptCount ?? 0
            );

            for (int i = 0; i < orderIds.Count; i++)
            {
                IThumbnailGenerationEngine engine = ResolveById(orderIds[i], selectedEngine);
                AddUnique(order, engine);
            }

            return order;
        }

        // テスト注入エンジンを優先しつつ、既知IDなら既定実体へ戻す。
        public IThumbnailGenerationEngine ResolveById(
            string engineId,
            IThumbnailGenerationEngine selectedEngine
        )
        {
            if (
                selectedEngine != null
                && string.Equals(selectedEngine.EngineId, engineId, StringComparison.OrdinalIgnoreCase)
            )
            {
                return selectedEngine;
            }

            if (string.Equals(engineId, autogenEngine.EngineId, StringComparison.OrdinalIgnoreCase))
            {
                return autogenEngine;
            }

            if (
                string.Equals(
                    engineId,
                    ffMediaToolkitEngine.EngineId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return ffMediaToolkitEngine;
            }

            if (
                string.Equals(
                    engineId,
                    ffmpegOnePassEngine.EngineId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return ffmpegOnePassEngine;
            }

            if (string.Equals(engineId, openCvEngine.EngineId, StringComparison.OrdinalIgnoreCase))
            {
                return openCvEngine;
            }

            return selectedEngine;
        }

        private static void AddUnique(
            List<IThumbnailGenerationEngine> order,
            IThumbnailGenerationEngine engine
        )
        {
            if (engine == null)
            {
                return;
            }

            for (int i = 0; i < order.Count; i++)
            {
                if (
                    string.Equals(
                        order[i].EngineId,
                        engine.EngineId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return;
                }
            }

            order.Add(engine);
        }
    }
}
