using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 管理者サービスの起動と再起動を面倒みる。
    /// UAC拒否や起動失敗の後は、その監視セッション中は再要求しない。
    /// </summary>
    internal sealed class AdminTelemetryServiceProcessManager
    {
        private readonly object syncRoot = new();
        private ManagedAdminServiceProcess currentProcess;
        private bool suppressRetryUntilNextSupervisorSession;

        public bool IsServiceAvailable()
        {
            return File.Exists(ResolveExecutablePath());
        }

        public async Task RunSupervisorAsync(Action<string> log, CancellationToken cts)
        {
            Action<string> safeLog = log ?? (_ => { });
            ResetRetrySuppressionForNewSupervisorSession();
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    EnsureServiceRunning(safeLog);
                    await Task.Delay(1000, cts).ConfigureAwait(false);
                }
            }
            finally
            {
                StopService(safeLog, "supervisor-stop");
            }
        }

        public void StopNow(Action<string> log = null)
        {
            StopService(log ?? (_ => { }), "manual-stop");
        }

        private void EnsureServiceRunning(Action<string> log)
        {
            string executablePath = ResolveExecutablePath();
            if (!File.Exists(executablePath))
            {
                return;
            }

            ManagedAdminServiceProcess existing = null;
            lock (syncRoot)
            {
                existing = currentProcess;
            }

            if (existing != null)
            {
                if (!existing.Process.HasExited)
                {
                    return;
                }

                StopService(log, "exited");
            }

            if (!CanAttemptStart())
            {
                return;
            }

            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = executablePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                };
                psi.ArgumentList.Add("--parent-pid");
                psi.ArgumentList.Add(Environment.ProcessId.ToString());

                // 非昇格本体からでも管理者サービスを立ち上げられるよう、ここでだけ昇格を要求する。
                if (!IsCurrentProcessAdministrator())
                {
                    psi.Verb = "runas";
                }

                Process process = Process.Start(psi);
                if (process == null)
                {
                    SuppressRetryUntilNextSupervisorSession();
                    log("admin telemetry service start failed: Process.Start returned null.");
                    return;
                }

                lock (syncRoot)
                {
                    currentProcess = new ManagedAdminServiceProcess(process);
                    suppressRetryUntilNextSupervisorSession = false;
                }
                log($"admin telemetry service started: pid={process.Id}");
            }
            catch (Win32Exception ex)
            {
                SuppressRetryUntilNextSupervisorSession();
                log($"admin telemetry service start skipped: {ex.Message}");
            }
            catch (Exception ex)
            {
                SuppressRetryUntilNextSupervisorSession();
                log($"admin telemetry service start failed: {ex.Message}");
            }
        }

        private void StopService(Action<string> log, string reason)
        {
            ManagedAdminServiceProcess managed = null;
            lock (syncRoot)
            {
                if (currentProcess == null)
                {
                    return;
                }

                managed = currentProcess;
                currentProcess = null;
            }

            try
            {
                if (!managed.Process.HasExited)
                {
                    managed.Process.Kill(entireProcessTree: true);
                    managed.Process.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                log($"admin telemetry service stop warning: reason={reason} err={ex.Message}");
            }
            finally
            {
                log($"admin telemetry service stopped: reason={reason}");
                managed.Process.Dispose();
            }
        }

        internal bool CanAttemptStart()
        {
            lock (syncRoot)
            {
                return !suppressRetryUntilNextSupervisorSession;
            }
        }

        internal void SuppressRetryUntilNextSupervisorSession()
        {
            lock (syncRoot)
            {
                suppressRetryUntilNextSupervisorSession = true;
            }
        }

        internal void ResetRetrySuppressionForNewSupervisorSession()
        {
            lock (syncRoot)
            {
                suppressRetryUntilNextSupervisorSession = false;
            }
        }

        private static bool IsCurrentProcessAdministrator()
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

        private static string ResolveExecutablePath()
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "admin-service",
                "IndigoMovieManager.AdminService.exe"
            );
        }

        private sealed class ManagedAdminServiceProcess
        {
            public ManagedAdminServiceProcess(Process process)
            {
                Process = process ?? throw new ArgumentNullException(nameof(process));
            }

            public Process Process { get; }
        }
    }
}
