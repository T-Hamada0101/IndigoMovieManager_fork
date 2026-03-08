using System.ComponentModel;
using System.IO.Pipes;

namespace IndigoMovieManager.Thumbnail.Ipc
{
    // 管理者サービスへの接続は毎回短命接続に固定し、切断復旧を単純化する。
    public sealed class NamedPipeAdminTelemetryClient : IAdminTelemetryClient
    {
        private readonly string pipeName;
        private readonly int connectTimeoutMs;
        private readonly int requestTimeoutMs;

        public NamedPipeAdminTelemetryClient()
            : this(
                ThumbnailIpcTransportPolicy.AdminServicePipeName,
                ThumbnailIpcTransportPolicy.ConnectTimeoutMs,
                ThumbnailIpcTransportPolicy.RequestTimeoutMs
            ) { }

        internal NamedPipeAdminTelemetryClient(
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

        public Task<AdminTelemetryServiceCapabilities> GetCapabilitiesAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            return SendAsync(
                new AdminTelemetryPipeRequest
                {
                    Command = AdminTelemetryPipeCommands.GetCapabilities,
                    RequestContext = requestContext ?? new(),
                },
                response => response.Capabilities,
                cancellationToken
            );
        }

        public Task<SystemLoadSnapshotDto> GetSystemLoadSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            return SendAsync(
                new AdminTelemetryPipeRequest
                {
                    Command = AdminTelemetryPipeCommands.GetSystemLoadSnapshot,
                    RequestContext = requestContext ?? new(),
                },
                response => response.SystemLoadSnapshot,
                cancellationToken
            );
        }

        public Task<DiskThermalSnapshotDto> GetDiskThermalSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            string diskId,
            CancellationToken cancellationToken
        )
        {
            return SendAsync(
                new AdminTelemetryPipeRequest
                {
                    Command = AdminTelemetryPipeCommands.GetDiskThermalSnapshot,
                    RequestContext = requestContext ?? new(),
                    DiskId = diskId ?? "",
                },
                response => response.DiskThermalSnapshot,
                cancellationToken
            );
        }

        public Task<UsnMftStatusDto> GetUsnMftStatusAsync(
            AdminTelemetryRequestContext requestContext,
            string volumeName,
            CancellationToken cancellationToken
        )
        {
            return SendAsync(
                new AdminTelemetryPipeRequest
                {
                    Command = AdminTelemetryPipeCommands.GetUsnMftStatus,
                    RequestContext = requestContext ?? new(),
                    VolumeName = volumeName ?? "",
                },
                response => response.UsnMftStatus,
                cancellationToken
            );
        }

        private async Task<T> SendAsync<T>(
            AdminTelemetryPipeRequest request,
            Func<AdminTelemetryPipeResponse, T> selector,
            CancellationToken cancellationToken
        )
        {
            using NamedPipeClientStream pipe = new(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );

            using CancellationTokenSource timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(connectTimeoutMs);
            try
            {
                await pipe.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException();
            }

            using CancellationTokenSource requestCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCts.CancelAfter(requestTimeoutMs);
            try
            {
                await NamedPipeMessageFraming.WriteAsync(pipe, request, requestCts.Token)
                    .ConfigureAwait(false);
                AdminTelemetryPipeResponse response = await NamedPipeMessageFraming
                    .ReadAsync<AdminTelemetryPipeResponse>(pipe, requestCts.Token)
                    .ConfigureAwait(false);
                ThrowIfError(response);
                return selector(response);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException();
            }
        }

        private static void ThrowIfError(AdminTelemetryPipeResponse response)
        {
            if (response == null || string.IsNullOrWhiteSpace(response.ErrorKind))
            {
                return;
            }

            if (
                string.Equals(
                    response.ErrorKind,
                    AdminTelemetryPipeErrorKinds.AccessDenied,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new UnauthorizedAccessException(response.ErrorMessage);
            }

            if (
                string.Equals(
                    response.ErrorKind,
                    AdminTelemetryPipeErrorKinds.InvalidRequest,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new ArgumentException(response.ErrorMessage);
            }

            throw new Win32Exception(response.ErrorMessage);
        }
    }
}
