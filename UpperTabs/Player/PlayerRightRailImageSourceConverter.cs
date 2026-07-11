using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using IndigoMovieManager.Converter;
using IndigoMovieManager.UpperTabs.Common;

namespace IndigoMovieManager.UpperTabs.Player
{
    /// <summary>
    /// Player右レールの画像要求を ImageRequest 語彙へ寄せる薄い入口。
    /// </summary>
    public sealed class PlayerRightRailImageSourceConverter : IMultiValueConverter
    {
        internal static event EventHandler<ImageRequest> ImageWarmCompleted;

        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            PlayerRightRailImageBurstMetrics.RecordConvert();
            if (values == null || values.Length < 3)
            {
                return DependencyProperty.UnsetValue;
            }

            object moviePathValue = values.Length > 3 ? values[3] : null;
            object revisionValue = values.Length > 4 ? values[4] : null;
            int requestRevision = UpperTabActivationGate.ResolveImageRequestRevision(revisionValue);
            ImageRequest request = UpperTabActivationGate.CreatePlayerRightRailImageRequest(
                values[0],
                values[2],
                moviePathValue,
                requestRevision
            );

            if (
                !UpperTabActivationGate.ShouldApplyPlayerRightRailImageRequest(
                    request,
                    requestRevision
                )
            )
            {
                return ResolveStalePlayerRightRailImageResult(
                    request,
                    requestRevision,
                    out _
                );
            }

            bool isExists = values[1] is not bool exists || exists;
            int decodePixelHeight = NoLockImageConverter.ResolveDecodePixelHeight(parameter);
            ImageDecodeRequest decodeRequest = NoLockImageConverter.BuildImageDecodeRequest(
                request,
                decodePixelHeight,
                "player-right-rail.background-warm"
            );
            if (
                NoLockImageConverter.TryGetCachedDecodeRequest(
                    decodeRequest,
                    isExists,
                    out NoLockImageConverter.ImageDecodeExecutionResult executionResult
                )
            )
            {
                PlayerRightRailImageBurstMetrics.RecordCacheHit();
                return ResolvePlayerRightRailDecodeImage(
                    executionResult.Image,
                    executionResult.DecodeResult
                );
            }

            PlayerRightRailImageBurstMetrics.RecordCacheMiss();
            PlayerRightRailImageWarmQueueResult queueResult = PlayerRightRailImageWarmQueue.Queue(
                decodeRequest,
                isExists,
                OnImageWarmCompleted
            );
            PlayerRightRailImageBurstMetrics.RecordQueueResult(queueResult);
            return DependencyProperty.UnsetValue;
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }

        internal static object ResolveStalePlayerRightRailImageResult(
            ImageRequest request,
            int currentRevision,
            out ImageDecodeResult decodeResult
        )
        {
            ImageLoadResult loadResult = ImageLoadResult.Canceled(
                request,
                currentRevision,
                "stale-player-right-rail",
                isStale: true
            );
            global::IndigoMovieManager.DebugRuntimeLog.Write(
                "ui-tempo",
                $"player {ImageLoadLogFields.Build(loadResult)} image_event=right-rail-request-discarded"
            );
            decodeResult = new ImageDecodeResult(
                loadResult,
                DecodeElapsedMilliseconds: 0,
                CacheHit: false
            );
            return DependencyProperty.UnsetValue;
        }

        private static object ResolvePlayerRightRailDecodeImage(
            object image,
            ImageDecodeResult decodeResult
        )
        {
            return decodeResult.ImageRequest.ThumbnailRole == ImageRequestThumbnailRole.PlayerRightRail
                ? image
                : DependencyProperty.UnsetValue;
        }

        private static void OnImageWarmCompleted(ImageRequest request)
        {
            try
            {
                ImageWarmCompleted?.Invoke(null, request);
            }
            catch
            {
                // 表示更新通知の失敗で背景warmを止めない。
            }
        }
    }

    internal enum PlayerRightRailImageWarmQueueResult
    {
        Enqueued,
        Duplicate,
        Suppressed,
    }

    internal readonly record struct PlayerRightRailImageBurstMetricsSnapshot(
        long SessionId,
        long ConvertCount,
        long CacheHitCount,
        long CacheMissCount,
        long QueueEnqueuedCount,
        long QueueDuplicateCount,
        long QueueSuppressedCount
    );

    internal static class PlayerRightRailImageBurstMetrics
    {
        private static BurstState _activeSession;

        internal static void Begin(long sessionId)
        {
            if (sessionId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionId));
            }

            // 状態ごと差し替え、古い処理の後着加算を新しいburstへ混ぜない。
            Interlocked.Exchange(ref _activeSession, new BurstState(sessionId));
        }

        internal static bool End(
            long sessionId,
            out PlayerRightRailImageBurstMetricsSnapshot snapshot
        )
        {
            BurstState activeSession = Volatile.Read(ref _activeSession);
            if (activeSession == null || activeSession.SessionId != sessionId)
            {
                snapshot = default;
                return false;
            }

            BurstState endedSession = Interlocked.CompareExchange(
                ref _activeSession,
                null,
                activeSession
            );
            if (!ReferenceEquals(endedSession, activeSession))
            {
                snapshot = default;
                return false;
            }

            snapshot = activeSession.CreateSnapshot();
            return true;
        }

        internal static void RecordConvert()
        {
            BurstState activeSession = Volatile.Read(ref _activeSession);
            activeSession?.RecordConvert();
        }

        internal static void RecordCacheHit()
        {
            BurstState activeSession = Volatile.Read(ref _activeSession);
            activeSession?.RecordCacheHit();
        }

        internal static void RecordCacheMiss()
        {
            BurstState activeSession = Volatile.Read(ref _activeSession);
            activeSession?.RecordCacheMiss();
        }

        internal static void RecordQueueResult(PlayerRightRailImageWarmQueueResult result)
        {
            BurstState activeSession = Volatile.Read(ref _activeSession);
            activeSession?.RecordQueueResult(result);
        }

        internal static void ResetForTesting()
        {
            Interlocked.Exchange(ref _activeSession, null);
        }

        private sealed class BurstState
        {
            private long _convertCount;
            private long _cacheHitCount;
            private long _cacheMissCount;
            private long _queueEnqueuedCount;
            private long _queueDuplicateCount;
            private long _queueSuppressedCount;

            internal BurstState(long sessionId)
            {
                SessionId = sessionId;
            }

            internal long SessionId { get; }

            internal void RecordConvert() => Interlocked.Increment(ref _convertCount);

            internal void RecordCacheHit() => Interlocked.Increment(ref _cacheHitCount);

            internal void RecordCacheMiss() => Interlocked.Increment(ref _cacheMissCount);

            internal void RecordQueueResult(PlayerRightRailImageWarmQueueResult result)
            {
                if (result == PlayerRightRailImageWarmQueueResult.Enqueued)
                {
                    Interlocked.Increment(ref _queueEnqueuedCount);
                    return;
                }

                if (result == PlayerRightRailImageWarmQueueResult.Duplicate)
                {
                    Interlocked.Increment(ref _queueDuplicateCount);
                    return;
                }

                Interlocked.Increment(ref _queueSuppressedCount);
            }

            internal PlayerRightRailImageBurstMetricsSnapshot CreateSnapshot()
            {
                return new PlayerRightRailImageBurstMetricsSnapshot(
                    SessionId,
                    Interlocked.Read(ref _convertCount),
                    Interlocked.Read(ref _cacheHitCount),
                    Interlocked.Read(ref _cacheMissCount),
                    Interlocked.Read(ref _queueEnqueuedCount),
                    Interlocked.Read(ref _queueDuplicateCount),
                    Interlocked.Read(ref _queueSuppressedCount)
                );
            }
        }
    }

    internal static class PlayerRightRailImageWarmQueue
    {
        internal const int Capacity = 64;
        private static readonly object Gate = new();
        private static readonly Queue<WarmRequest> Pending = new();
        private static readonly HashSet<string> PendingKeys = new(StringComparer.OrdinalIgnoreCase);
        private static bool _workerActive;
        private static Func<bool> _isSuspendedProvider;

        internal static void SetSuspensionProvider(Func<bool> isSuspendedProvider)
        {
            lock (Gate)
            {
                _isSuspendedProvider = isSuspendedProvider;
            }
        }

        internal static PlayerRightRailImageWarmQueueResult Queue(
            ImageDecodeRequest decodeRequest,
            bool isExists,
            Action<ImageRequest> completed
        )
        {
            lock (Gate)
            {
                if (IsSuspendedSafely())
                {
                    return PlayerRightRailImageWarmQueueResult.Suppressed;
                }

                string key = BuildKey(decodeRequest, isExists);
                if (!PendingKeys.Add(key))
                {
                    return PlayerRightRailImageWarmQueueResult.Duplicate;
                }

                while (Pending.Count >= Capacity)
                {
                    WarmRequest removed = Pending.Dequeue();
                    PendingKeys.Remove(removed.Key);
                }

                Pending.Enqueue(new WarmRequest(key, decodeRequest, isExists, completed));
                if (_workerActive)
                {
                    return PlayerRightRailImageWarmQueueResult.Enqueued;
                }

                _workerActive = true;
                _ = System.Threading.Tasks.Task.Run(ProcessAsync);
                return PlayerRightRailImageWarmQueueResult.Enqueued;
            }
        }

        private static bool IsSuspendedSafely()
        {
            try
            {
                return _isSuspendedProvider?.Invoke() == true;
            }
            catch
            {
                // 判定失敗時は画像要求を止めず、従来どおりwarmへ進める。
                return false;
            }
        }

        private static void ProcessAsync()
        {
            while (true)
            {
                WarmRequest request;
                lock (Gate)
                {
                    if (Pending.Count == 0)
                    {
                        _workerActive = false;
                        return;
                    }

                    request = Pending.Dequeue();
                }

                long decodeStartedTimestamp = Stopwatch.GetTimestamp();
                try
                {
                    NoLockImageConverter.ImageDecodeExecutionResult result =
                        NoLockImageConverter.ConvertDecodeRequest(
                            request.DecodeRequest,
                            request.IsExists
                        );
                    WriteDecodeSample(
                        new ImageDecodePlanResult(
                            request.DecodeRequest,
                            result.DecodeResult,
                            DecodeAttempted: true
                        )
                    );
                    if (!ReferenceEquals(result.Image, Binding.DoNothing))
                    {
                        request.Completed(request.DecodeRequest.ImageRequest);
                    }
                }
                catch (Exception ex)
                {
                    ImageLoadResult loadResult = ImageLoadResult.Failed(
                        request.DecodeRequest.ImageRequest,
                        request.DecodeRequest.RequestRevision,
                        $"background-warm-{ex.GetType().Name}",
                        usesPlaceholder: false,
                        hasResolvedImage: false
                    );
                    WriteDecodeSample(
                        new ImageDecodePlanResult(
                            request.DecodeRequest,
                            new ImageDecodeResult(
                                loadResult,
                                DecodeElapsedMilliseconds: (long)
                                    Stopwatch.GetElapsedTime(decodeStartedTimestamp)
                                        .TotalMilliseconds,
                                CacheHit: false
                            ),
                            DecodeAttempted: true
                        )
                    );
                    // 個別画像の失敗は次の要求へ進める。
                }
                finally
                {
                    lock (Gate)
                    {
                        PendingKeys.Remove(request.Key);
                    }
                }
            }
        }

        private static void WriteDecodeSample(ImageDecodePlanResult result)
        {
            if (!PlayerRightRailImageWarmLogSampler.TryAccept(result))
            {
                return;
            }

            global::IndigoMovieManager.DebugRuntimeLog.Write(
                "ui-tempo",
                $"player image warm {ImageDecodePlanLogFields.Build(result)}"
            );
        }

        private static string BuildKey(ImageDecodeRequest request, bool isExists)
        {
            return $"{request.ImageRequest.ThumbnailPath}|{isExists}|{request.DecodePixelHeight}";
        }

        private readonly record struct WarmRequest(
            string Key,
            ImageDecodeRequest DecodeRequest,
            bool IsExists,
            Action<ImageRequest> Completed
        );
    }

    internal static class PlayerRightRailImageWarmLogSampler
    {
        private static int _successLogged;
        private static int _failureLogged;

        internal static bool TryAccept(ImageDecodePlanResult result)
        {
            ref int sampleSlot = ref (
                result.ImageLoadResult.Outcome == ImageLoadOutcome.Ready
                    ? ref _successLogged
                    : ref _failureLogged
            );
            return System.Threading.Interlocked.CompareExchange(ref sampleSlot, 1, 0) == 0;
        }

        internal static void ResetForTesting()
        {
            System.Threading.Interlocked.Exchange(ref _successLogged, 0);
            System.Threading.Interlocked.Exchange(ref _failureLogged, 0);
        }
    }
}
