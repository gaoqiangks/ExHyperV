using System.IO;
using System.Text.Json;
namespace ExHyperV.Services;
internal sealed class StartupVmOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
}
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
    // Null denotes legacy settings; an explicit empty list disables all startup connections.
    public List<StartupVmOption>? VirtualMachines { get; set; }
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExHyperV", "AutoConnect.json");
    public static AutoConnectOptions Load()
    {
        if (!File.Exists(ConfigPath)) return new();
        try { return Parse(File.ReadAllText(ConfigPath)); }
        catch (Exception ex) { AutoConnectLog.Write("Settings read failed: " + ex.Message); return new(); }
    }
    internal static AutoConnectOptions Parse(string json) => JsonSerializer.Deserialize<AutoConnectOptions>(json) ?? new();
    public List<StartupVmOption> StartupTargets()
    {
        if (VirtualMachines != null) return VirtualMachines.Where(v => v.Enabled).ToList();
        return Enabled && !string.IsNullOrWhiteSpace(VmName)
            ? [new StartupVmOption { Name = VmName, Enabled = true }] : [];
    }
    private static bool Matches(StartupVmOption vm, Guid id, string name) =>
        !string.IsNullOrWhiteSpace(vm.Id)
            ? Guid.TryParse(vm.Id, out var savedId) && savedId == id
            : string.Equals(vm.Name, name, StringComparison.OrdinalIgnoreCase);
    public bool IsEnabled(Guid id, string name) => StartupTargets().Any(v => Matches(v, id, name));
    public void SetEnabled(Guid id, string name, bool enabled)
    {
        VirtualMachines ??= StartupTargets();
        var item = VirtualMachines.FirstOrDefault(v => Matches(v, id, name));
        if (item == null) { item = new StartupVmOption(); VirtualMachines.Add(item); }
        item.Id = id.ToString(); item.Name = name; item.Enabled = enabled;
        Enabled = VirtualMachines.Any(v => v.Enabled);
    }
    public void Save()
    {
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
