using System;
using System.Diagnostics;
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
            PersistenceWriteRequest writeRequest = BuildApplicationSettingsWriteRequest(reason);
            if (App.IsDiagnosticNoPersistEnabled())
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"application settings save skipped: {writeRequest.BuildLogFields()} diagnostic_no_persist=1"
                );
                return;
            }

            lock (_applicationSettingsSaveSync)
            {
                _applicationSettingsSaveTask = _applicationSettingsSaveTask.ContinueWith(
                    _ => SaveApplicationSettingsInBackground(writeRequest),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default
                );
            }
        }

        private static PersistenceWriteRequest BuildApplicationSettingsWriteRequest(string reason)
        {
            return PersistenceWriteRequest.Create(
                PersistenceWriteKind.ApplicationSettings,
                reason,
                "application-settings",
                retryable: true
            );
        }

        private void SaveApplicationSettingsInBackground(PersistenceWriteRequest writeRequest)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                if (App.IsDiagnosticNoPersistEnabled())
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"application settings save skipped in background: {writeRequest.BuildLogFields()} diagnostic_no_persist=1"
                    );
                    return;
                }

                lock (_applicationSettingsSaveSync)
                {
                    Properties.Settings.Default.Save();
                }

                PersistenceWriteResult result = PersistenceWriteResult.FromSuccess(
                    writeRequest,
                    stopwatch.Elapsed
                );
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"application settings save succeeded: {result.LogFields}"
                );
            }
            catch (Exception ex)
            {
                PersistenceWriteResult result = PersistenceWriteResult.FromFailure(
                    writeRequest,
                    stopwatch.Elapsed,
                    PersistenceFailureKind.ApplicationSettings
                );
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"application settings save failed: {result.LogFields} err='{ex.GetType().Name}: {ex.Message}'"
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
