using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using ExHyperV.Views;

namespace ExHyperV.Services;

// Automatic connection is opt-in through saved settings or --auto-connect.
internal static class AutoConnectStartup
{
    private static Mutex? instanceMutex;
    private static bool started;
    private static string logFile = "";
    private sealed record VmStatus(string Id, ushort State, ushort Enhanced);

    private static void Log(string message)
    {
        try { File.AppendAllText(logFile, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
        catch { }
    }

    private static VmStatus? Query(string name)
    {
        string escaped = name.Replace("\\", "\\\\").Replace("'", "\\'");
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(@"\\.\root\virtualization\v2"),
            new ObjectQuery($"SELECT Name, EnabledState, EnhancedSessionModeState FROM Msvm_ComputerSystem WHERE ElementName = '{escaped}'"),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(10) });
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
                return new(item["Name"].ToString()!, Convert.ToUInt16(item["EnabledState"]), Convert.ToUInt16(item["EnhancedSessionModeState"] ?? 0));
        }
        return null;
    }

    internal static async Task RunAsync(MainWindow owner)
    {
        var args = Environment.GetCommandLineArgs();
        int flag = Array.IndexOf(args, "--auto-connect");
        var options = AutoConnectOptions.Load();
        if (args.Contains("--settings")) { new AutoConnectSettingsWindow { Owner = owner }.Show(); return; }
        if (args.Contains("--no-auto-connect") || started || (flag < 0 && !options.Enabled)) return;
        started = true;
        if (flag >= 0 && (flag + 1 >= args.Length || string.IsNullOrWhiteSpace(args[flag + 1])))
        {
            System.Windows.MessageBox.Show("--auto-connect 后必须提供虚拟机名称。", "自动连接配置错误");
            return;
        }
        string name = flag >= 0 ? args[flag + 1] : options.VmName;
        string logDirectory = Path.GetDirectoryName(AutoConnectLog.FilePath)!;
        Directory.CreateDirectory(logDirectory);
        logFile = Path.Combine(logDirectory, "autoconnect.log");
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)));
        instanceMutex = new Mutex(true, @"Local\ExHyperV-AutoConnect-" + key, out bool acquired);
        if (!acquired) { Log("Already running; duplicate launch ignored."); owner.Close(); return; }
        Log($"Starting auto-connect: {name}; local auto-connect extension.");
        owner.Title = $"ExHyperV — 正在等待 {name} 启动";
        owner.WindowState = WindowState.Minimized;
        try
        {
            var timeout = Stopwatch.StartNew();
            VmStatus? status = null;
            bool startRequested = false;
            double? runningSince = null;
            while (timeout.Elapsed < TimeSpan.FromMinutes(5))
            {
                status = await Task.Run(() => Query(name));
                if (status != null)
                {
                    if ((status.State is 3 or 6 or 32769 or 32779 or 32783) && !startRequested)
                    {
                        startRequested = true;
                        Log("VM is off/saved; requesting a normal start.");
                        var result = await VmPowerService.ExecuteControlActionAsync(name, "Start");
                        if (!result.Success) throw new InvalidOperationException(result.Error);
                    }
                    if (status.State == 2)
                    {
                        runningSince ??= timeout.Elapsed.TotalSeconds;
                        // Prefer guest enhanced-session readiness, but keep basic console usable
                        // when the guest does not support enhanced sessions.
                        if (status.Enhanced == 2 || timeout.Elapsed.TotalSeconds - runningSince.Value >= 60)
                            break;
                    }
                    else runningSince = null;
                }
                await Task.Delay(2000);
            }
            if (status?.State != 2)
                throw new TimeoutException($"等待 {name} 启动超过 5 分钟。请检查 Hyper-V 管理器。虚拟机不会被强制关机或重置。");
            Log($"VM running: {status.Id}; enhanced session state={status.Enhanced}.");
            var console = new ConsoleWindow(status.Id, name);
            console.RdpHost.Connected += () =>
            {
                Log("RDP console connected.");
                console.Dispatcher.InvokeAsync(() =>
                {
                    console.ApplyStartupWindowMode(options.WindowMode);
                    if (!options.CloseMainAfterConnect || !owner.IsVisible) return;
                    System.Windows.Application.Current.MainWindow = console;
                    owner.Close();
                    Log("Main window closed; console remains open.");
                    console.Activate();
                });
            };
            console.ContentRendered += (_, _) =>
            {
                console.ApplyStartupWindowMode(options.WindowMode);
                Log("Console window mode applied: " + options.WindowMode);
            };
            console.Closed += (_, _) => owner.Close();
            console.Show();
            console.Activate();
            owner.Title = "ExHyperV";
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex);
            owner.WindowState = WindowState.Normal;
            owner.Title = "ExHyperV — 自动连接失败";
            System.Windows.MessageBox.Show(owner, ex.Message + "\n\n日志：" + logFile, "ExHyperV 自动连接", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
