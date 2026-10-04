using System.IO;
using System.Text.Json;
namespace ExHyperV.Services;
internal sealed class AutoConnectOptions
{
    public bool Enabled { get; set; }
    public bool OpenMainWhenConnected { get; set; } = true;
    public bool PreferEnhancedSession { get; set; } = true;
    public bool DefaultVmManagementPage { get; set; } = true;
    public string VmName { get; set; } = "";
    public string WindowMode { get; set; } = "Maximized";
    public bool CloseMainAfterConnect { get; set; } = true;
    public bool PasswordlessAutoLogin { get; set; }
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExHyperV", "AutoConnect.json");
    public static AutoConnectOptions Load()
    {
        if (!File.Exists(ConfigPath)) return new();
        try { return JsonSerializer.Deserialize<AutoConnectOptions>(File.ReadAllText(ConfigPath)) ?? new(); }
        catch (Exception ex) { AutoConnectLog.Write("Settings read failed: " + ex.Message); return new(); }
    }
    public void Save()
    {
        if (string.IsNullOrWhiteSpace(VmName)) throw new ArgumentException("请填写虚拟机名称。");
        if (WindowMode is not ("Normal" or "Maximized" or "Minimized" or "FullScreen")) throw new ArgumentException("窗口状态无效。");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        string temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, ConfigPath, true);
    }
}
internal static class AutoConnectLog
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExHyperV", "logs", "autoconnect.log");
    public static void Write(string message)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
        catch { }
    }
}
