namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// UI優先中の新規lease取得だけを延期し、解除後に再開する。
    /// </summary>
    internal sealed class ThumbnailLeaseDeferralGate
    {
        private readonly Func<bool> _shouldDeferLeaseResolver;
        private readonly int _pollIntervalMs;
        private readonly Action<string> _log;
        private bool _isDeferred;

        public ThumbnailLeaseDeferralGate(
            Func<bool> shouldDeferLeaseResolver,
            int pollIntervalMs,
            Action<string> log
        )
        {
            _shouldDeferLeaseResolver = shouldDeferLeaseResolver;
            _pollIntervalMs = Math.Max(100, pollIntervalMs);
            _log = log ?? (_ => { });
        }

        public bool ShouldDeferLease()
        {
            if (_shouldDeferLeaseResolver == null)
            {
                return false;
            }

            bool shouldDefer;
            try
            {
                shouldDefer = _shouldDeferLeaseResolver();
            }
            catch (Exception ex)
            {
                // 判定失敗で従来処理を止め続けない。既存互換を優先してlease取得を継続する。
                _log(
                    $"consumer lease defer resolver failed: policy=continue type={ex.GetType().Name} message={ex.Message}"
                );
                if (_isDeferred)
                {
                    _isDeferred = false;
                    _log("consumer lease resumed: reason=resolver-failed policy=continue");
                }
                return false;
            }

            if (shouldDefer && !_isDeferred)
            {
                _isDeferred = true;
                _log("consumer lease deferred: reason=user-priority");
            }
            else if (!shouldDefer && _isDeferred)
            {
                _isDeferred = false;
                _log("consumer lease resumed: reason=user-priority-released");
            }

            return shouldDefer;
        }

        public async Task WaitUntilLeaseAllowedAsync(CancellationToken cancellationToken)
        {
            while (ShouldDeferLease())
            {
                await Task.Delay(_pollIntervalMs, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
