using System.ComponentModel;
using System.IO.Pipes;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class NamedPipeAdminTelemetryClientTests
{
    [Test]
    public async Task GetCapabilitiesAsync_実pipe応答を受信できる()
    {
        string pipeName = $"IndigoMovieManager.AdminTelemetry.Tests.{Guid.NewGuid():N}";
        using CancellationTokenSource serverCts = new(TimeSpan.FromSeconds(5));
        Task serverTask = RunSingleResponseServerAsync(
            pipeName,
            request =>
            {
                Assert.That(request.Command, Is.EqualTo(AdminTelemetryPipeCommands.GetCapabilities));
                return Task.FromResult(
                    new AdminTelemetryPipeResponse
                    {
                        Capabilities = new AdminTelemetryServiceCapabilities
                        {
                            ServiceVersion = "test-admin-service",
                            SupportsWatcherIntegration = true,
                        },
                    }
                );
            },
            serverCts.Token
        );
        NamedPipeAdminTelemetryClient client = new(pipeName, 1000, 1000);

        AdminTelemetryServiceCapabilities result = await client.GetCapabilitiesAsync(
            new AdminTelemetryRequestContext
            {
                ConsumerKind = AdminTelemetryConsumerKind.WatcherFacade,
            },
            CancellationToken.None
        );

        Assert.That(result.ServiceVersion, Is.EqualTo("test-admin-service"));
        Assert.That(result.SupportsWatcherIntegration, Is.True);
        await serverTask;
    }

    [Test]
    public void GetCapabilitiesAsync_接続先不在はTimeoutExceptionを投げる()
    {
        NamedPipeAdminTelemetryClient client = new(
            $"IndigoMovieManager.AdminTelemetry.Tests.{Guid.NewGuid():N}",
            100,
            100
        );

        Assert.ThrowsAsync<TimeoutException>(
            async () =>
                await client.GetCapabilitiesAsync(
                    new AdminTelemetryRequestContext(),
                    CancellationToken.None
                )
        );
    }

    [Test]
    public void GetUsnMftStatusAsync_InternalError応答はWin32Exceptionを投げる()
    {
        string pipeName = $"IndigoMovieManager.AdminTelemetry.Tests.{Guid.NewGuid():N}";
        using CancellationTokenSource serverCts = new(TimeSpan.FromSeconds(5));
        Task serverTask = RunSingleResponseServerAsync(
            pipeName,
            _ =>
                Task.FromResult(
                    new AdminTelemetryPipeResponse
                    {
                        ErrorKind = AdminTelemetryPipeErrorKinds.InternalError,
                        ErrorMessage = "boom",
                    }
                ),
            serverCts.Token
        );
        NamedPipeAdminTelemetryClient client = new(pipeName, 1000, 1000);

        Assert.ThrowsAsync<Win32Exception>(
            async () =>
                await client.GetUsnMftStatusAsync(
                    new AdminTelemetryRequestContext(),
                    @"C:\",
                    CancellationToken.None
                )
        );

        serverTask.GetAwaiter().GetResult();
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
