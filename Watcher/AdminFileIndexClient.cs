using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager.Watcher
{
    // Watcher から管理者サービスの file index API を叩く薄い client。
    internal sealed class AdminFileIndexClient : IAdminFileIndexClient
    {
        private readonly IAdminTelemetryClient telemetryClient;
        private readonly IAdminFileIndexTransport transport;

        public AdminFileIndexClient()
            : this(new NamedPipeAdminTelemetryClient(), new NamedPipeAdminFileIndexTransport()) { }

        internal AdminFileIndexClient(
            IAdminTelemetryClient telemetryClient,
            IAdminFileIndexTransport transport
        )
        {
            this.telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public AvailabilityResult CheckAvailability()
        {
            try
            {
                AdminTelemetryServiceCapabilities capabilities = telemetryClient
                    .GetCapabilitiesAsync(
                        AdminTelemetryRuntimeResolver.CreateWatcherRequestContext(),
                        CancellationToken.None
                    )
                    .GetAwaiter()
                    .GetResult();
                if (capabilities.SupportsWatcherIntegration)
                {
                    return new AvailabilityResult(true, EverythingReasonCodes.Ok);
                }

                return new AvailabilityResult(
                    false,
                    $"{EverythingReasonCodes.AvailabilityErrorPrefix}AdminServiceUnavailable"
                );
            }
            catch (Exception ex)
            {
                return new AvailabilityResult(false, EverythingReasonCodes.BuildAvailabilityError(ex));
            }
        }

        public FileIndexMovieResult CollectMoviePaths(FileIndexQueryOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            AdminTelemetryPipeRequest request = new()
            {
                Command = AdminTelemetryPipeCommands.CollectMoviePaths,
                RequestContext = AdminTelemetryRuntimeResolver.CreateWatcherRequestContext(),
                FileIndexQuery = new AdminFileIndexQueryDto
                {
                    RootPath = options.RootPath ?? "",
                    IncludeSubdirectories = options.IncludeSubdirectories,
                    CheckExt = options.CheckExt ?? "",
                    ChangedSinceUtc = options.ChangedSinceUtc,
                },
            };

            AdminTelemetryPipeResponse response = transport.Send(request);
            ThrowIfError(response);
            AdminFileIndexMovieResultDto dto = response.FileIndexMovieResult ?? new();
            return new FileIndexMovieResult(
                dto.Success,
                dto.MoviePaths ?? [],
                dto.MaxObservedChangedUtc,
                dto.Reason ?? ""
            );
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

            throw new InvalidOperationException(response.ErrorMessage);
        }
    }
}
