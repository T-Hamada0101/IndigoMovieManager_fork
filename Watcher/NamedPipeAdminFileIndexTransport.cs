using System.IO.Pipes;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager.Watcher
{
    // Watcher から管理者サービスの file index pipe を叩く既定 transport。
    internal sealed class NamedPipeAdminFileIndexTransport : IAdminFileIndexTransport
    {
        private readonly string pipeName;
        private readonly int connectTimeoutMs;
        private readonly int requestTimeoutMs;

        public NamedPipeAdminFileIndexTransport()
            : this(
                ThumbnailIpcTransportPolicy.AdminServicePipeName,
                ThumbnailIpcTransportPolicy.ConnectTimeoutMs,
                ThumbnailIpcTransportPolicy.RequestTimeoutMs
            ) { }

        internal NamedPipeAdminFileIndexTransport(
            string pipeName,
            int connectTimeoutMs,
            int requestTimeoutMs
        )
        {
            this.pipeName = string.IsNullOrWhiteSpace(pipeName)
                ? throw new ArgumentException("pipe name is required.", nameof(pipeName))
                : pipeName;
            this.connectTimeoutMs = connectTimeoutMs > 0
                ? connectTimeoutMs
                : throw new ArgumentOutOfRangeException(nameof(connectTimeoutMs));
            this.requestTimeoutMs = requestTimeoutMs > 0
                ? requestTimeoutMs
                : throw new ArgumentOutOfRangeException(nameof(requestTimeoutMs));
        }

        public AdminTelemetryPipeResponse Send(AdminTelemetryPipeRequest request)
        {
            using NamedPipeClientStream pipe = new(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );

            using CancellationTokenSource connectCts = new();
            connectCts.CancelAfter(connectTimeoutMs);
            try
            {
                pipe.ConnectAsync(connectCts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException();
            }

            using CancellationTokenSource requestCts = new();
            requestCts.CancelAfter(requestTimeoutMs);
            try
            {
                NamedPipeMessageFraming.WriteAsync(pipe, request, requestCts.Token)
                    .GetAwaiter()
                    .GetResult();
                return NamedPipeMessageFraming.ReadAsync<AdminTelemetryPipeResponse>(
                        pipe,
                        requestCts.Token
                    )
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException();
            }
        }
    }
}
