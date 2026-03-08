using System.IO.Pipes;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager
{
    internal sealed class AdminTelemetryServiceHost
    {
        private const int WarningTemperatureCelsius = 55;
        private const int CriticalTemperatureCelsius = 65;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            List<Task> connectionTasks = [];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    NamedPipeServerStream server = new(
                        ThumbnailIpcTransportPolicy.AdminServicePipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous
                    );

                    try
                    {
                        await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        server.Dispose();
                        throw;
                    }

                    Task connectionTask = HandleConnectionAsync(server, cancellationToken);
                    lock (connectionTasks)
                    {
                        connectionTasks.Add(connectionTask);
                    }

                    _ = connectionTask.ContinueWith(
                        _ =>
                        {
                            lock (connectionTasks)
                            {
                                connectionTasks.Remove(connectionTask);
                            }
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default
                    );
                }
            }
            finally
            {
                Task[] snapshot;
                lock (connectionTasks)
                {
                    snapshot = [.. connectionTasks];
                }

                await Task.WhenAll(snapshot).ConfigureAwait(false);
            }
        }

        private async Task HandleConnectionAsync(
            NamedPipeServerStream server,
            CancellationToken cancellationToken
        )
        {
            await using (server)
            {
                AdminTelemetryPipeResponse response;
                try
                {
                    AdminTelemetryPipeRequest request = await NamedPipeMessageFraming
                        .ReadAsync<AdminTelemetryPipeRequest>(server, cancellationToken)
                        .ConfigureAwait(false);
                    response = HandleRequest(request);
                }
                catch (UnauthorizedAccessException ex)
                {
                    response = CreateErrorResponse(
                        AdminTelemetryPipeErrorKinds.AccessDenied,
                        ex.Message
                    );
                }
                catch (ArgumentException ex)
                {
                    response = CreateErrorResponse(
                        AdminTelemetryPipeErrorKinds.InvalidRequest,
                        ex.Message
                    );
                }
                catch (Exception ex)
                {
                    response = CreateErrorResponse(
                        AdminTelemetryPipeErrorKinds.InternalError,
                        ex.Message
                    );
                }

                await NamedPipeMessageFraming.WriteAsync(server, response, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private AdminTelemetryPipeResponse HandleRequest(AdminTelemetryPipeRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Command))
            {
                throw new ArgumentException("pipe request command is required.");
            }

            return request.Command switch
            {
                AdminTelemetryPipeCommands.GetCapabilities => new AdminTelemetryPipeResponse
                {
                    Capabilities = BuildCapabilities(),
                },
                AdminTelemetryPipeCommands.GetSystemLoadSnapshot => new AdminTelemetryPipeResponse
                {
                    SystemLoadSnapshot = new SystemLoadSnapshotDto
                    {
                        // 並列制御の根幹はまだ本体の内部値を採用する。
                        // ここは service 経由導入の足場だけ返す。
                        CapturedAtUtc = DateTime.UtcNow,
                    },
                },
                AdminTelemetryPipeCommands.GetDiskThermalSnapshot => new AdminTelemetryPipeResponse
                {
                    DiskThermalSnapshot = BuildDiskThermalSnapshot(request.DiskId),
                },
                AdminTelemetryPipeCommands.GetUsnMftStatus => new AdminTelemetryPipeResponse
                {
                    UsnMftStatus = BuildUsnMftStatus(request.VolumeName),
                },
                AdminTelemetryPipeCommands.CollectMoviePaths => new AdminTelemetryPipeResponse
                {
                    FileIndexMovieResult = AdminFileIndexService.CollectMoviePaths(
                        request.FileIndexQuery
                    ),
                },
                _ => throw new ArgumentException($"unknown pipe command: {request.Command}"),
            };
        }

        private static AdminTelemetryServiceCapabilities BuildCapabilities()
        {
            bool isAdministrator = IsAdministrator();
            return new AdminTelemetryServiceCapabilities
            {
                ServiceVersion = "admin-service-v1",
                RequiresElevation = true,
                SupportsSystemLoad = false,
                SupportsDiskThermal = isAdministrator && AdminDiskThermalService.IsSupportedEnvironment(),
                SupportsUsnMftStatus = isAdministrator,
                SupportsWatcherIntegration = isAdministrator,
                CapturedAtUtc = DateTime.UtcNow,
            };
        }

        private static DiskThermalSnapshotDto BuildDiskThermalSnapshot(string diskId)
        {
            if (!IsAdministrator())
            {
                throw new UnauthorizedAccessException("admin telemetry service requires elevation.");
            }

            return AdminDiskThermalService.BuildSnapshot(
                diskId,
                WarningTemperatureCelsius,
                CriticalTemperatureCelsius
            );
        }

        private static UsnMftStatusDto BuildUsnMftStatus(string volumeName)
        {
            if (!IsAdministrator())
            {
                return new UsnMftStatusDto
                {
                    VolumeName = NormalizeRootPath(volumeName),
                    Available = false,
                    StatusKind = UsnMftStatusKind.AccessDenied,
                    CapturedAtUtc = DateTime.UtcNow,
                };
            }

            string normalizedVolumeName = NormalizeRootPath(volumeName);
            if (string.IsNullOrWhiteSpace(normalizedVolumeName))
            {
                return new UsnMftStatusDto
                {
                    VolumeName = "",
                    Available = false,
                    StatusKind = UsnMftStatusKind.Unavailable,
                    CapturedAtUtc = DateTime.UtcNow,
                };
            }

            try
            {
                DriveInfo drive = new(normalizedVolumeName);
                bool available =
                    drive.IsReady
                    && drive.DriveType == DriveType.Fixed
                    && string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
                return new UsnMftStatusDto
                {
                    VolumeName = normalizedVolumeName,
                    Available = available,
                    LastScanLatencyMs = 0,
                    JournalBacklogCount = 0,
                    StatusKind = available ? UsnMftStatusKind.Ready : UsnMftStatusKind.Unavailable,
                    CapturedAtUtc = DateTime.UtcNow,
                };
            }
            catch
            {
                return new UsnMftStatusDto
                {
                    VolumeName = normalizedVolumeName,
                    Available = false,
                    StatusKind = UsnMftStatusKind.Unavailable,
                    CapturedAtUtc = DateTime.UtcNow,
                };
            }
        }

        private static AdminTelemetryPipeResponse CreateErrorResponse(
            string errorKind,
            string errorMessage
        )
        {
            return new AdminTelemetryPipeResponse
            {
                ErrorKind = errorKind ?? AdminTelemetryPipeErrorKinds.InternalError,
                ErrorMessage = errorMessage ?? "",
            };
        }

        private static string NormalizeRootPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            try
            {
                return Path.GetPathRoot(Path.GetFullPath(value.Trim())) ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                using System.Security.Principal.WindowsIdentity identity =
                    System.Security.Principal.WindowsIdentity.GetCurrent();
                if (identity == null)
                {
                    return false;
                }

                System.Security.Principal.WindowsPrincipal principal = new(identity);
                return principal.IsInRole(
                    System.Security.Principal.WindowsBuiltInRole.Administrator
                );
            }
            catch
            {
                return false;
            }
        }
    }
}
