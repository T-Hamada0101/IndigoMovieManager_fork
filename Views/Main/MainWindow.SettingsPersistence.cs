using System;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly object _applicationSettingsSaveSync = new();
        private Task _applicationSettingsSaveTask = Task.CompletedTask;

        // Settings.Save はファイルI/Oなので、UIの切替・終了導線から直列の背景保存へ逃がす。
        private void QueueApplicationSettingsSave(string reason)
        {
            lock (_applicationSettingsSaveSync)
            {
                _applicationSettingsSaveTask = _applicationSettingsSaveTask.ContinueWith(
                    _ => SaveApplicationSettingsInBackground(reason),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default
                );
            }
        }

        private void SaveApplicationSettingsInBackground(string reason)
        {
            try
            {
                lock (_applicationSettingsSaveSync)
                {
                    Properties.Settings.Default.Save();
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"application settings save failed: reason={reason ?? ""} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        // 終了時だけは保存取りこぼしを避けるため、並走させた保存を短時間だけ回収する。
        private void WaitForApplicationSettingsSaveForShutdown(string reason, int timeoutMs = 1000)
        {
            Task saveTask;
            lock (_applicationSettingsSaveSync)
            {
                saveTask = _applicationSettingsSaveTask;
            }

            try
            {
                if (!saveTask.Wait(Math.Max(0, timeoutMs)))
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"application settings save drain timeout: reason={reason ?? ""} timeout_ms={timeoutMs}"
                    );
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"application settings save drain failed: reason={reason ?? ""} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }
    }
}
