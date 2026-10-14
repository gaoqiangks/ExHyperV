namespace ExHyperV.Models;

public sealed record DesktopScaleOption(int Percent)
{
    public string Label => Percent == 0 ? DesktopScaleText.Automatic : $"{Percent}%";
}

public static class DesktopScaleText
{
    private static string Text(string key) => Properties.Resources.ResourceManager.GetString(
        "DesktopScale_" + key, Properties.Resources.Culture) ?? key;
    public static string Automatic => Text(nameof(Automatic));
    public static string Title => Text(nameof(Title));
    public static string Hint => Text(nameof(Hint));
    public static string Description => Text(nameof(Description));
    public static string LinuxHint => Text(nameof(LinuxHint));
    public static string VirtualMachine => Text(nameof(VirtualMachine));
    public static string Save => Text(nameof(Save));
    public static string SaveFailed => Text(nameof(SaveFailed));
}
