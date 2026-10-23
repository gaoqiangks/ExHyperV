using System.Windows;
using System.Windows.Threading;
using System.IO;
using System.Xml.Linq;

// Each scenario needs its own process because WPF permits one Application per AppDomain.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] is not ("manager-only" or "manager-first" or "console-first" or "auto-connect"))
        {
            Console.Error.WriteLine("Specify manager-only, manager-first, console-first, or auto-connect.");
            return 2;
        }

        // Use the production XAML setting with real WPF windows, without loading app services.
        var appSettings = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ExHyperV.App.xaml"));
        var shutdownMode = Enum.Parse<ShutdownMode>(appSettings.Root!.Attribute("ShutdownMode")!.Value);
        var app = new LifetimeProbe(args[0]) { ShutdownMode = shutdownMode };
        app.Run();
        if (app.Failure != null || !app.Completed || !app.Exited)
        {
            Console.Error.WriteLine(app.Failure ?? "Application exited before the last expected window was closed.");
            return 1;
        }

        Console.WriteLine($"PASS {args[0]}: expected windows survived; application exited after the last window closed.");
        return 0;
    }
}

internal sealed class LifetimeProbe(string scenario) : Application
{
    public string? Failure { get; private set; }
    public bool Completed { get; private set; }
    public bool Exited { get; private set; }

    // Do not start production services or connect to actual virtual machines.
    protected override void OnStartup(StartupEventArgs e)
    {
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timeout.Tick += (_, _) => { Failure = "Timed out waiting for the last window to close."; Shutdown(1); };
        timeout.Start();
        Step(() =>
        {
            Require(ShutdownMode == ShutdownMode.OnLastWindowClose, "App.xaml must use OnLastWindowClose.");
            var manager = OpenWindow();
            MainWindow = manager;
            if (scenario == "manager-only")
            {
                Completed = true;
                manager.Close();
                return;
            }

            var firstConsole = OpenWindow();
            var secondConsole = OpenWindow();
            if (scenario == "console-first")
            {
                firstConsole.Close();
                secondConsole.Close();
                Step(() =>
                {
                    Require(manager.IsVisible && Windows.Count == 1, "Closing consoles must preserve the manager.");
                    Completed = true;
                    manager.Close();
                });
                return;
            }

            if (scenario == "auto-connect") MainWindow = firstConsole;
            manager.Close();
            Step(() =>
            {
                Require(firstConsole.IsVisible && secondConsole.IsVisible && Windows.Count == 2,
                    "Closing the manager must preserve both consoles.");
                firstConsole.Close();
                Step(() =>
                {
                    Require(secondConsole.IsVisible && Windows.Count == 1,
                        "Closing one console must preserve the other, including when it is MainWindow.");
                    Completed = true;
                    secondConsole.Close();
                });
            });
        });
    }

    // Record actual WPF application exit, not only Window.Closed events.
    protected override void OnExit(ExitEventArgs e) => Exited = true;

    private static Window OpenWindow()
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Opacity = 0 };
        window.Show();
        return window;
    }

    private void Step(Action action) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
    {
        try { action(); }
        catch (Exception ex)
        {
            Failure = ex.ToString();
            Shutdown(1);
        }
    }));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}