using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; } = true;
    public bool DefaultVmManagementPage { get; set; } = true;
    public string ConsoleToolbarPosition { get; set; } = "Left";
    // Empty follows the application theme; custom colors are opaque #RRGGBB values.
    public string ConsoleToolbarBackground { get; set; } = "";
    public ConsoleToolbarNameOptions ConsoleToolbarName { get; set; } = new();
    internal static event Action? Saved;
    public string VmName { get; set; } = "";
    public string WindowMode { get; set; } = "Maximized";
    public bool CloseMainAfterConnect { get; set; } = true;
    public bool PasswordlessAutoLogin { get; set; }
    // Null preserves the old global preference until individual VM choices are saved.
    public Dictionary<string, bool>? PasswordlessAutoLoginByVm { get; set; }
    // Null denotes legacy settings; an explicit empty list disables all startup connections.
    public List<StartupVmOption>? VirtualMachines { get; set; }
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExHyperV", "AutoConnect.json");
    public static AutoConnectOptions Load()
    {
        if (!File.Exists(ConfigPath)) return new();
        try { return Parse(File.ReadAllText(ConfigPath)); }
        catch (Exception ex) { AutoConnectLog.Write("Settings read failed: " + ex.Message); return new(); }
    }
    internal static AutoConnectOptions Parse(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root) return new();
        string position = "Left";
        if (root.TryGetPropertyValue(nameof(ConsoleToolbarPosition), out var positionNode))
        {
            if (positionNode is JsonValue positionValue
                && positionValue.TryGetValue<string>(out var savedPosition)
                && IsValidToolbarPosition(savedPosition)) position = savedPosition;
        }
        else if (root.TryGetPropertyValue("ConsoleToolbarOnLeft", out var legacyNode)
            && legacyNode is JsonValue legacyValue
            && legacyValue.TryGetValue<bool>(out bool onLeft)) position = onLeft ? "Left" : "Top";
        root[nameof(ConsoleToolbarPosition)] = position;
        string background = "";
        if (root[nameof(ConsoleToolbarBackground)] is JsonValue backgroundValue
            && backgroundValue.TryGetValue<string>(out var savedBackground))
            TryNormalizeToolbarBackground(savedBackground, out background);
        root[nameof(ConsoleToolbarBackground)] = background;
        root[nameof(ConsoleToolbarName)] = JsonSerializer.SerializeToNode(
            ConsoleToolbarNameOptions.Parse(root[nameof(ConsoleToolbarName)]));
        if (root[nameof(PasswordlessAutoLoginByVm)] is { } loginNode)
        {
            var logins = new JsonObject();
            if (loginNode is JsonObject entries)
                foreach (var entry in entries)
                    if (Guid.TryParse(entry.Key, out var vmId) && entry.Value is JsonValue value
                        && value.TryGetValue<bool>(out bool enabled)) logins[vmId.ToString("D")] = enabled;
            root[nameof(PasswordlessAutoLoginByVm)] = logins;
        }
        return root.Deserialize<AutoConnectOptions>() ?? new();
    }
    internal bool IsPasswordlessLoginSelected(string vmId) => PasswordlessAutoLoginByVm is null
        ? PasswordlessAutoLogin
        : Guid.TryParse(vmId, out var id) && PasswordlessAutoLoginByVm.TryGetValue(id.ToString("D"), out bool enabled) && enabled;
    internal bool IsPasswordlessLoginEnabled(string vmId) => PasswordlessAutoLogin && IsPasswordlessLoginSelected(vmId);
    internal static bool TryNormalizeToolbarBackground(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value)) return true;
        string hex = value.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length != 6 || !hex.All(char.IsAsciiHexDigit)) return false;
        normalized = "#" + hex.ToUpperInvariant();
        return true;
    }
    private static bool IsValidToolbarPosition(string? position) => position is "Top" or "Left" or "Right" or "Bottom";
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
        if (!IsValidToolbarPosition(ConsoleToolbarPosition)) throw new ArgumentException("控制栏位置无效。");
        if (!TryNormalizeToolbarBackground(ConsoleToolbarBackground, out string background))
            throw new ArgumentException("控制栏背景色无效，请输入 #RRGGBB 格式的颜色，例如 #202020。");
        ConsoleToolbarBackground = background;
        ConsoleToolbarName ??= new();
        if (!ConsoleToolbarName.TryValidate(out string nameError)) throw new ArgumentException(nameError);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        string temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, ConfigPath, true);
        Saved?.Invoke();
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
