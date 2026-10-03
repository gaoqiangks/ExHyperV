using System.Windows;
using ExHyperV.Tools;
using ExHyperV.Views;
namespace ExHyperV.Services;
internal static class PasswordlessLogin
{
    internal static void Attach(ConsoleWindow window, RdpClientHost host, string vmName)
    {
        CancellationTokenSource? attempt = null;
        bool loggedIn = false;
        host.LoginCompleted += () => { loggedIn = true; attempt?.Cancel(); AutoConnectLog.Write($"Guest login confirmed: {vmName}"); };
        host.Disconnected += _ => { attempt?.Cancel(); loggedIn = false; };
        window.Closed += (_, _) => attempt?.Cancel();
        host.Connected += () => window.Dispatcher.InvokeAsync(async () =>
        {
            var options = AutoConnectOptions.Load();
            if (!options.PasswordlessAutoLogin || !string.Equals(options.VmName, vmName, StringComparison.OrdinalIgnoreCase)) return;
            attempt?.Cancel();
            attempt = new CancellationTokenSource();
            var token = attempt.Token;
            loggedIn = false;
            try
            {
                await Task.Delay(2500, token);
                for (int i = 0; i < 2 && !loggedIn; i++)
                {
                    if (host.ConnectionState != 1) return;
                    host.SendGuestEnter();
                    AutoConnectLog.Write($"Passwordless login Enter {i + 1}/2 sent to guest: {vmName}");
                    await Task.Delay(1400, token);
                }
                if (!loggedIn) AutoConnectLog.Write($"Login not yet confirmed; no more input will be sent: {vmName}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AutoConnectLog.Write("Passwordless login failed: " + ex); }
        });
    }
}