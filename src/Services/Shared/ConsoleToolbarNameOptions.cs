using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ExHyperV.Services;

internal sealed class ConsoleToolbarNameOptions
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);
    public bool Enabled { get; set; }
    public string Pattern { get; set; } = "";
    public string Replacement { get; set; } = "$1";
    public Dictionary<string, string> Aliases { get; set; } = new();

    internal static ConsoleToolbarNameOptions Parse(JsonNode? node)
    {
        var options = new ConsoleToolbarNameOptions();
        if (node is not JsonObject root) return options;
        if (root[nameof(Enabled)] is JsonValue enabled && enabled.TryGetValue<bool>(out bool value))
            options.Enabled = value;
        if (root[nameof(Pattern)] is JsonValue pattern && pattern.TryGetValue<string>(out var expression))
            options.Pattern = expression ?? "";
        if (root[nameof(Replacement)] is JsonValue replacement && replacement.TryGetValue<string>(out var format))
            options.Replacement = format ?? "$1";
        if (root[nameof(Aliases)] is JsonObject aliases)
            foreach (var alias in aliases)
                if (Guid.TryParse(alias.Key, out var id) && alias.Value is JsonValue text
                    && text.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name))
                    options.Aliases[id.ToString("D")] = name.Trim();
        return options;
    }

    internal bool TryValidate(out string error)
    {
        error = "";
        if (!Enabled || string.IsNullOrWhiteSpace(Pattern)) return true;
        try { _ = CreateRegex(); return true; }
        catch (ArgumentException ex) { error = "控制栏名称的正则表达式无效：" + ex.Message; return false; }
    }

    internal string GetDisplayName(string vmId, string vmName) => GetDisplayName(vmId, vmName, out _);

    internal string GetDisplayName(string vmId, string vmName, out string note)
    {
        note = "";
        if (!Enabled) return vmName;
        // Aliases follow the stable VM ID, including after the real VM is renamed.
        if (Guid.TryParse(vmId, out var id) && Aliases.TryGetValue(id.ToString("D"), out var alias)
            && !string.IsNullOrWhiteSpace(alias)) return alias.Trim();
        if (string.IsNullOrWhiteSpace(Pattern)) return vmName;
        try
        {
            var match = CreateRegex().Match(vmName);
            if (!match.Success) { note = "未匹配，使用原名"; return vmName; }
            string display = match.Result(Replacement ?? "$1").Trim();
            if (string.IsNullOrWhiteSpace(display)) { note = "结果为空，使用原名"; return vmName; }
            return display;
        }
        catch (RegexMatchTimeoutException) { note = "规则超时，使用原名"; return vmName; }
        catch (ArgumentException) { note = "规则无效，使用原名"; return vmName; }
    }

    private Regex CreateRegex() => new(Pattern, RegexOptions.CultureInvariant, MatchTimeout);
}
