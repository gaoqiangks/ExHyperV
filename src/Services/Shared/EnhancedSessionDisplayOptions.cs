using System.IO;
using System.Text.Json;

namespace ExHyperV.Services;

internal sealed class EnhancedSessionDisplayOptions
{
    internal static event Action<string, int>? Changed;
    // Zero (or an absent entry) follows the host. VM IDs survive VM renames.
    public Dictionary<string, int> DesktopScalePercentByVm { get; set; } = new();
    public static int[] Presets => [0, 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "ExHyperV", "EnhancedSessionDisplay.json");

    internal static EnhancedSessionDisplayOptions Parse(string json)
    {
        var options = JsonSerializer.Deserialize<EnhancedSessionDisplayOptions>(json) ?? new();
        options.DesktopScalePercentByVm = (options.DesktopScalePercentByVm ?? new())
            .Where(p => Guid.TryParse(p.Key, out _) && p.Value is >= 100 and <= 500)
            .GroupBy(p => Guid.Parse(p.Key).ToString("D"))
            .ToDictionary(g => g.Key, g => g.Last().Value);
        return options;
    }

    public static EnhancedSessionDisplayOptions Load()
    {
        try { return File.Exists(ConfigPath) ? Parse(File.ReadAllText(ConfigPath)) : new(); }
        catch (Exception ex) { AutoConnectLog.Write("Display settings read failed: " + ex.Message); return new(); }
    }

    public int GetPercent(string vmId) => Guid.TryParse(vmId, out var id) &&
        DesktopScalePercentByVm.TryGetValue(id.ToString("D"), out int percent) ? percent : 0;

    public void SetPercent(string vmId, int percent)
    {
        if (!Guid.TryParse(vmId, out var id)) throw new ArgumentException("Invalid VM ID.", nameof(vmId));
        if (percent != 0 && percent is not (>= 100 and <= 500))
            throw new ArgumentOutOfRangeException(nameof(percent));
        if (percent == 0) DesktopScalePercentByVm.Remove(id.ToString("D"));
        else DesktopScalePercentByVm[id.ToString("D")] = percent;
    }

    internal static uint ResolvePercent(int percent, double hostDpiScale)
    {
        if (percent is >= 100 and <= 500) return (uint)percent;
        if (!double.IsFinite(hostDpiScale)) return 100;
        return (uint)Math.Clamp(Math.Round(hostDpiScale * 100), 100, 500);
    }

    public void Save(string? changedVmId = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        string temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, ConfigPath, true);
        if (changedVmId != null) Changed?.Invoke(changedVmId, GetPercent(changedVmId));
    }
}
