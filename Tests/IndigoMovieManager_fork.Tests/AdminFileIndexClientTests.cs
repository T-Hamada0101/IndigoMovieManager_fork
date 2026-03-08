using IndigoMovieManager.Thumbnail.Ipc;
using IndigoMovieManager.Watcher;
using System.IO.Pipes;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class AdminFileIndexClientTests
{
    [Test]
    public void CheckAvailability_Watcher対応ありならOkを返す()
    {
        AdminFileIndexClient client = new(
            new FakeAdminTelemetryClient
            {
                Capabilities = new AdminTelemetryServiceCapabilities
                {
                    SupportsWatcherIntegration = true,
                },
            },
            new FakeAdminFileIndexTransport()
        );

        AvailabilityResult result = client.CheckAvailability();

        Assert.That(result.CanUse, Is.True);
        Assert.That(result.Reason, Is.EqualTo(EverythingReasonCodes.Ok));
    }

    [Test]
    public void CheckAvailability_Watcher対応なしならUnavailableを返す()
    {
        AdminFileIndexClient client = new(
            new FakeAdminTelemetryClient
            {
                Capabilities = new AdminTelemetryServiceCapabilities
                {
                    SupportsWatcherIntegration = false,
                },
            },
            new FakeAdminFileIndexTransport()
        );

        AvailabilityResult result = client.CheckAvailability();

        Assert.That(result.CanUse, Is.False);
        Assert.That(
            result.Reason,
            Is.EqualTo($"{EverythingReasonCodes.AvailabilityErrorPrefix}AdminServiceUnavailable")
        );
    }

    [Test]
    public void CheckAvailability_TimeoutはAvailabilityErrorへ変換する()
    {
        AdminFileIndexClient client = new(
            new FakeAdminTelemetryClient { GetCapabilitiesException = new TimeoutException() },
            new FakeAdminFileIndexTransport()
        );

        AvailabilityResult result = client.CheckAvailability();

        Assert.That(result.CanUse, Is.False);
        Assert.That(
            result.Reason,
            Is.EqualTo($"{EverythingReasonCodes.AvailabilityErrorPrefix}TimeoutException")
        );
    }

    [Test]
    public void CollectMoviePaths_Success時はDtoをそのまま返す()
    {
        FakeAdminFileIndexTransport transport = new()
        {
            Response = new AdminTelemetryPipeResponse
            {
                FileIndexMovieResult = new AdminFileIndexMovieResultDto
                {
                    Success = true,
                    MoviePaths = [@"C:\movies\a.mp4", @"C:\movies\b.mp4"],
                    MaxObservedChangedUtc = new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc),
                    Reason = "ok:provider=usnmft",
                },
            },
        };
        AdminFileIndexClient client = new(new FakeAdminTelemetryClient(), transport);

        FileIndexMovieResult result = client.CollectMoviePaths(
            new FileIndexQueryOptions
            {
                RootPath = @"C:\movies",
                IncludeSubdirectories = true,
                CheckExt = "*.mp4",
            }
        );

        Assert.That(result.Success, Is.True);
        Assert.That(result.MoviePaths.Count, Is.EqualTo(2));
        Assert.That(result.Reason, Is.EqualTo("ok:provider=usnmft"));
        Assert.That(transport.SendCallCount, Is.EqualTo(1));
        Assert.That(
            transport.LastRequest?.FileIndexQuery.RootPath,
            Is.EqualTo(@"C:\movies")
        );
    }

    [Test]
    public void CollectMoviePaths_AccessDenied応答はUnauthorizedAccessExceptionへ変換する()
    {
        AdminFileIndexClient client = new(
            new FakeAdminTelemetryClient(),
            new FakeAdminFileIndexTransport
            {
                Response = new AdminTelemetryPipeResponse
                {
                    ErrorKind = AdminTelemetryPipeErrorKinds.AccessDenied,
                    ErrorMessage = "denied",
                },
            }
        );

        Assert.Throws<UnauthorizedAccessException>(
            () =>
                client.CollectMoviePaths(
                    new FileIndexQueryOptions
                    {
                        RootPath = @"C:\movies",
                        IncludeSubdirectories = true,
                        CheckExt = "*.mp4",
                    }
                )
        );
    }

    [Test]
    public void CollectMoviePaths_InvalidRequest応答はArgumentExceptionへ変換する()
    {
        AdminFileIndexClient client = new(
            new FakeAdminTelemetryClient(),
            new FakeAdminFileIndexTransport
            {
                Response = new AdminTelemetryPipeResponse
                {
                    ErrorKind = AdminTelemetryPipeErrorKinds.InvalidRequest,
                    ErrorMessage = "bad request",
                },
            }
        );

        Assert.Throws<ArgumentException>(
            () =>
                client.CollectMoviePaths(
                    new FileIndexQueryOptions
                    {
                        RootPath = @"C:\movies",
                        IncludeSubdirectories = true,
                        CheckExt = "*.mp4",
                    }
                )
        );
    }

    [Test]
    public void CollectMoviePaths_InternalError応答はInvalidOperationExceptionへ変換する()
    {
        AdminFileIndexClient client = new(
            new FakeAdminTelemetryClient(),
            new FakeAdminFileIndexTransport
            {
                Response = new AdminTelemetryPipeResponse
                {
                    ErrorKind = AdminTelemetryPipeErrorKinds.InternalError,
                    ErrorMessage = "boom",
                },
            }
        );

        Assert.Throws<InvalidOperationException>(
            () =>
                client.CollectMoviePaths(
                    new FileIndexQueryOptions
                    {
                        RootPath = @"C:\movies",
                        IncludeSubdirectories = true,
                        CheckExt = "*.mp4",
                    }
                )
        );
    }

    [Test]
    public void CollectMoviePaths_TransportTimeoutはそのままTimeoutExceptionを投げる()
    {
        AdminFileIndexClient client = new(
            new FakeAdminTelemetryClient(),
            new FakeAdminFileIndexTransport { SendException = new TimeoutException() }
        );

        Assert.Throws<TimeoutException>(
            () =>
                client.CollectMoviePaths(
                    new FileIndexQueryOptions
                    {
                        RootPath = @"C:\movies",
                        IncludeSubdirectories = true,
                        CheckExt = "*.mp4",
                    }
                )
        );
    }

    [Test]
    public void NamedPipeTransport_実pipe往復で応答を受信できる()
    {
        string pipeName = $"IndigoMovieManager.AdminTelemetry.Tests.{Guid.NewGuid():N}";
        using CancellationTokenSource serverCts = new(TimeSpan.FromSeconds(5));
        Task serverTask = RunSingleResponseServerAsync(
            pipeName,
            request =>
            {
                Assert.That(request.Command, Is.EqualTo(AdminTelemetryPipeCommands.CollectMoviePaths));
                Assert.That(request.FileIndexQuery.RootPath, Is.EqualTo(@"C:\movies"));
                return Task.FromResult(
                    new AdminTelemetryPipeResponse
                    {
                        FileIndexMovieResult = new AdminFileIndexMovieResultDto
                        {
                            Success = true,
                            MoviePaths = [@"C:\movies\a.mp4"],
                            Reason = "ok:provider=usnmft",
                        },
                    }
                );
            },
            serverCts.Token
        );
        NamedPipeAdminFileIndexTransport transport = new(pipeName, 1000, 1000);

        AdminTelemetryPipeResponse response = transport.Send(
            new AdminTelemetryPipeRequest
            {
                Command = AdminTelemetryPipeCommands.CollectMoviePaths,
                FileIndexQuery = new AdminFileIndexQueryDto
                {
                    RootPath = @"C:\movies",
                    IncludeSubdirectories = true,
                    CheckExt = "*.mp4",
                },
            }
        );

        Assert.That(response.FileIndexMovieResult.Success, Is.True);
        Assert.That(response.FileIndexMovieResult.MoviePaths, Is.EqualTo([@"C:\movies\a.mp4"]));
        Assert.That(response.FileIndexMovieResult.Reason, Is.EqualTo("ok:provider=usnmft"));
        serverTask.GetAwaiter().GetResult();
    }

    [Test]
    public void NamedPipeTransport_接続先不在はTimeoutExceptionを投げる()
    {
        NamedPipeAdminFileIndexTransport transport = new(
            $"IndigoMovieManager.AdminTelemetry.Tests.{Guid.NewGuid():N}",
            100,
            100
        );

        Assert.Throws<TimeoutException>(
            () =>
                transport.Send(
                    new AdminTelemetryPipeRequest
                    {
                        Command = AdminTelemetryPipeCommands.CollectMoviePaths,
                    }
                )
        );
    }

    [Test]
    public void NamedPipeTransport_応答遅延はTimeoutExceptionを投げる()
    {
        string pipeName = $"IndigoMovieManager.AdminTelemetry.Tests.{Guid.NewGuid():N}";
        using CancellationTokenSource serverCts = new(TimeSpan.FromSeconds(5));
        Task serverTask = RunSingleResponseServerAsync(
            pipeName,
            async _ =>
            {
                await Task.Delay(500, serverCts.Token);
                return new AdminTelemetryPipeResponse();
            },
            serverCts.Token
        );
        NamedPipeAdminFileIndexTransport transport = new(pipeName, 1000, 100);

        Assert.Throws<TimeoutException>(
            () =>
                transport.Send(
                    new AdminTelemetryPipeRequest
                    {
                        Command = AdminTelemetryPipeCommands.CollectMoviePaths,
                    }
                )
        );

        serverCts.Cancel();
        try
        {
            serverTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // 要求タイムアウト後の server 側キャンセルは正常扱いにする。
        }
    }

    private sealed class FakeAdminTelemetryClient : IAdminTelemetryClient
    {
        public AdminTelemetryServiceCapabilities Capabilities { get; set; } = new();
        public Exception? GetCapabilitiesException { get; set; }

        public Task<AdminTelemetryServiceCapabilities> GetCapabilitiesAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            if (GetCapabilitiesException != null)
            {
                throw GetCapabilitiesException;
            }

            return Task.FromResult(Capabilities);
        }

        public Task<SystemLoadSnapshotDto> GetSystemLoadSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<DiskThermalSnapshotDto> GetDiskThermalSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            string diskId,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<UsnMftStatusDto> GetUsnMftStatusAsync(
            AdminTelemetryRequestContext requestContext,
            string volumeName,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeAdminFileIndexTransport : IAdminFileIndexTransport
    {
        public AdminTelemetryPipeResponse Response { get; set; } = new();
        public Exception? SendException { get; set; }
        public AdminTelemetryPipeRequest? LastRequest { get; private set; }
        public int SendCallCount { get; private set; }

        public AdminTelemetryPipeResponse Send(AdminTelemetryPipeRequest request)
        {
            SendCallCount++;
            LastRequest = request;
            if (SendException != null)
            {
                throw SendException;
            }

            return Response;
        }
    }

    private static async Task RunSingleResponseServerAsync(
        string pipeName,
        Func<AdminTelemetryPipeRequest, Task<AdminTelemetryPipeResponse>> responseFactory,
        CancellationToken cancellationToken
    )
    {
        await using NamedPipeServerStream server = new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
        );

        await server.WaitForConnectionAsync(cancellationToken);
        AdminTelemetryPipeRequest request = await NamedPipeMessageFraming.ReadAsync<AdminTelemetryPipeRequest>(
            server,
            cancellationToken
        );
        AdminTelemetryPipeResponse response = await responseFactory(request);
        await NamedPipeMessageFraming.WriteAsync(server, response, cancellationToken);
    }
}
