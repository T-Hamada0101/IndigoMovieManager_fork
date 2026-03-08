using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager.Watcher
{
    // file index 要求だけを送る transport を分離し、Watcher 側の失敗変換をテスト可能にする。
    internal interface IAdminFileIndexTransport
    {
        AdminTelemetryPipeResponse Send(AdminTelemetryPipeRequest request);
    }
}
