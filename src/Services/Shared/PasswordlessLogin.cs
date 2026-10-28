using System.Windows;
using ExHyperV.Tools;
using ExHyperV.Views;
namespace ExHyperV.Services;
internal static class PasswordlessLogin
{
    internal static void Attach(ConsoleWindow window, RdpClientHost host, string vmId, string vmName, Func<bool> isEnhancedSession)
    {
        CancellationTokenSource? attempt = null;
        bool loggedIn = false;
        void CancelAttempt()
        {
            attempt?.Cancel();
            attempt?.Dispose();
            attempt = null;
        }
        host.LoginCompleted += () =>
        {
            // A basic Hyper-V console reports a transport logon even while Windows is at Sign in.
            if (!isEnhancedSession()) return;
            loggedIn = true;
            CancelAttempt();
            AutoConnectLog.Write($"Guest login confirmed: {vmName}");
        };
        host.Disconnected += _ => { CancelAttempt(); loggedIn = false; };
        window.Closed += (_, _) => CancelAttempt();
        host.Connected += () =>
        {
            CancelAttempt();
            attempt = new CancellationTokenSource();
            var token = attempt.Token;
            window.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    if (token.IsCancellationRequested || loggedIn || EnhancedSessionCredentials.IsConfigured(vmId)) return;
                    // Resolve the saved VM ID, so renaming a guest does not change its login choice.
                    if (!AutoConnectOptions.Load().IsPasswordlessLoginEnabled(vmId)) return;
                    await Task.Delay(2500, token);
                    for (int i = 0; i < 2 && !loggedIn; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (host.ConnectionState != 1 || !AutoConnectOptions.Load().IsPasswordlessLoginEnabled(vmId)) return;
                        try
                        {
                            // Each console must activate its own input control when several VMs connect.
                            window.Activate();
                            if (loggedIn || token.IsCancellationRequested) return;
                            host.SendGuestEnter();
                            AutoConnectLog.Write($"Passwordless login Enter {i + 1}/2 sent to guest: {vmName}");
                        }
                        catch (System.Runtime.InteropServices.COMException ex)
                        {
                            AutoConnectLog.Write($"Passwordless login input not ready ({i + 1}/2): {vmName}; 0x{ex.HResult:X8}");
                        }
                        await Task.Delay(1400, token);
                    }
                    if (!loggedIn) AutoConnectLog.Write($"Login not yet confirmed; no more input will be sent: {vmName}");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { AutoConnectLog.Write("Passwordless login failed: " + ex); }
            });
        };
    }
}
