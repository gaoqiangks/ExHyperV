using System.Globalization;

namespace ExHyperV.Properties;

public static class ConsoleSessionText
{
    private static bool Chinese => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
    public static string Mode => Chinese ? "会话" : "Mode";
    public static string ModeHint => Chinese ? "切换基本会话 / 增强会话" : "Switch basic / enhanced session";
    public static string ResolutionHint => Chinese ? "分辨率（仅增强会话可用）；切换模式请使用“会话”按钮" : "Resolution (enhanced session only); use the Mode button to switch sessions";
}
