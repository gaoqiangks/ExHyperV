using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows;
using ExHyperV.Views;

namespace ExHyperV.Services;

// Automatic connection is opt-in through saved settings or --auto-connect.
internal static class AutoConnectStartup
{
    private static bool started;
    private static string logFile = "";
    private sealed record VmStatus(string Id, string Name, ushort State, ushort Enhanced);

    private static void Log(string message)
    {
        try { File.AppendAllText(logFile, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
        catch { }
    }

    private static VmStatus? Query(StartupVmOption vm)
    {
        string escaped = vm.Name.Replace("\\", "\\\\").Replace("'", "\\'");
        string predicate = Guid.TryParse(vm.Id, out var id) ? $"Name = '{id}'" : $"ElementName = '{escaped}'";
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(@"\\.\root\virtualization\v2"),
            new ObjectQuery($"SELECT Name, ElementName, EnabledState, EnhancedSessionModeState FROM Msvm_ComputerSystem WHERE {predicate}"),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(10) });
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
                return new(item["Name"].ToString()!, item["ElementName"].ToString()!, Convert.ToUInt16(item["EnabledState"]), Convert.ToUInt16(item["EnhancedSessionModeState"] ?? 0));
        }
        return null;
    }

    internal static async Task RunAsync(MainWindow owner)
    {
        var args = Environment.GetCommandLineArgs();
        int flag = Array.IndexOf(args, "--auto-connect");
        if (args.Contains("--settings")) { new AutoConnectSettingsWindow { Owner = owner }.Show(); return; }
        if (started) return;
        if (args.Contains("--no-auto-connect")) { owner.ShowStartupPage(); return; }
        var options = AutoConnectOptions.Load();
        var targets = options.StartupTargets();
        if (flag < 0 && targets.Count == 0) { owner.ShowStartupPage(); return; }
        started = true;
        if (flag >= 0 && (flag + 1 >= args.Length || string.IsNullOrWhiteSpace(args[flag + 1]) || args[flag + 1].StartsWith("--")))
        {
            System.Windows.MessageBox.Show("--auto-connect 后必须提供虚拟机名称。", "自动连接配置错误");
            return;
        }
        if (flag >= 0) targets = [new StartupVmOption { Name = args[flag + 1], Enabled = true }];
        string logDirectory = Path.GetDirectoryName(AutoConnectLog.FilePath)!;
        Directory.CreateDirectory(logDirectory);
        logFile = Path.Combine(logDirectory, "autoconnect.log");
        using var reservation = ConnectionPresence.TryReserveAutomatic();
        if (reservation == null)
        {
            if (!options.OpenMainWhenConnected) { owner.Close(); return; }
            Log("A connection instance already exists; opening the management window.");
            owner.Title = "ExHyperV";
            owner.WindowState = WindowState.Normal;
            owner.ShowStartupPage();
            return;
        }
        owner.WindowState = WindowState.Minimized;
        bool finishedStarting = false;
        bool connected = false;
        bool failed = false;
        var consoles = new List<ConsoleWindow>();
        void CloseManagerIfReady()
        {
            if (!finishedStarting || !connected || failed || !options.CloseMainAfterConnect || !owner.IsVisible || !consoles.Any(c => c.IsVisible)) return;
            // Multiple checked VMs must stay alive when any individual console is closed.
            System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnLastWindowClose;
            System.Windows.Application.Current.MainWindow = consoles.First(c => c.IsVisible);
            owner.Close();
            Log("Main window closed; consoles remain open.");
        }
        foreach (var target in targets)
        {
            string name = target.Name;
            Log($"Starting auto-connect: {name}; VM management selection.");
            owner.Title = $"ExHyperV — 正在等待 {name} 启动";
            try
            {
                var timeout = Stopwatch.StartNew();
                VmStatus? status = null;
                bool startRequested = false;
                double? runningSince = null;
                while (timeout.Elapsed < TimeSpan.FromMinutes(5))
                {
                    status = await Task.Run(() => Query(target));
                    if (status != null)
                    {
                        name = status.Name;
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
                consoles.Add(console);
                console.RdpHost.Connected += () =>
                {
                    Log("RDP console connected.");
                    console.Dispatcher.InvokeAsync(() =>
                    {
                        console.ApplyStartupWindowMode(options.WindowMode);
                        connected = true;
                        CloseManagerIfReady();
                        console.Activate();
                    });
                };
                console.ContentRendered += (_, _) =>
                {
                    console.ApplyStartupWindowMode(options.WindowMode);
                    Log("Console window mode applied: " + options.WindowMode);
                };
                console.Show();
                console.Activate();
                owner.Title = "ExHyperV";
                if (!options.CloseMainAfterConnect) owner.WindowState = WindowState.Normal;
            }
            catch (Exception ex)
            {
                failed = true;
                Log("ERROR: " + ex);
                owner.WindowState = WindowState.Normal;
                owner.Title = "ExHyperV — 自动连接失败";
                System.Windows.MessageBox.Show(owner, ex.Message + "\n\n日志：" + logFile, "ExHyperV 自动连接", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finishedStarting = true;
        owner.Title = "ExHyperV";
        if (failed || !options.CloseMainAfterConnect) owner.WindowState = WindowState.Normal;
        CloseManagerIfReady();
    }
}
