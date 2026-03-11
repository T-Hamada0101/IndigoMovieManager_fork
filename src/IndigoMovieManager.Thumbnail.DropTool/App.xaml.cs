using System.Windows;

namespace IndigoMovieManager.Thumbnail.DropTool
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            int exitCode = DropToolLauncher.Run(e?.Args ?? []);
            Shutdown(exitCode);
        }
    }
}
