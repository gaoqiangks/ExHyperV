using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ExHyperV.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;

namespace ExHyperV.Views;

internal sealed class ConsoleToolbarNameEditor : StackPanel
{
    private readonly CheckBox _enabled;
    private readonly TextBox _pattern;
    private readonly TextBox _replacement;
    private readonly StackPanel _body;
    private readonly StackPanel _vmList;
    private readonly TextBlock _error;
    private readonly Brush _foreground;
    private readonly ConsoleToolbarNameOptions _initial;
    private readonly Dictionary<string, (string Name, TextBox Alias, TextBlock Preview)> _rows = new();
    private readonly DispatcherTimer _previewTimer;

    internal ConsoleToolbarNameEditor(ConsoleToolbarNameOptions options, Brush foreground)
    {
        _initial = options;
        _foreground = foreground;
        Margin = new Thickness(0, 0, 0, 16);
        Children.Add(new TextBlock { Text = "虚拟机连接窗口的控制栏显示名称", Foreground = foreground });
        _enabled = new CheckBox { Content = "启用自定义显示名称", Foreground = foreground, IsChecked = options.Enabled, Margin = new Thickness(0, 8, 0, 8) };
        Children.Add(_enabled);
        _body = new StackPanel();
        Children.Add(_body);
        _body.Children.Add(Label("统一正则表达式（可选）"));
        _pattern = new TextBox { Text = options.Pattern, Margin = new Thickness(0, 4, 0, 8), ToolTip = "例如 ^Windows11(.+)$，提取 Windows11 后面的部分" };
        _body.Children.Add(_pattern);
        _body.Children.Add(Label("显示格式（$0 为匹配内容，$1 为第一个分组）"));
        _replacement = new TextBox { Text = options.Replacement, Margin = new Thickness(0, 4, 0, 8), ToolTip = "例如 $1；也支持 ${name} 形式的命名分组" };
        _body.Children.Add(_replacement);
        _error = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        _body.Children.Add(_error);
        _body.Children.Add(Label("示例：表达式 ^Windows11(.+)$，显示格式 $1，可将 Windows11SZU 显示为 SZU。"));
        _body.Children.Add(new TextBlock { Text = "单台自定义名称（优先于正则，留空使用统一规则或原名）", Foreground = foreground, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 6) });
        _vmList = new StackPanel();
        _vmList.Children.Add(Label("正在读取虚拟机…"));
        _body.Children.Add(_vmList);
        Children.Add(new TextBlock { Text = "只修改控制栏中的文字；悬停可查看完整原名。规则未匹配、结果为空或超时时使用原名。保存后立即应用。", Foreground = foreground, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RefreshPreview(); };
        _pattern.TextChanged += (_, _) => QueuePreview();
        _replacement.TextChanged += (_, _) => QueuePreview();
        _enabled.Checked += (_, _) => RefreshPreview();
        _enabled.Unchecked += (_, _) => RefreshPreview();
        Unloaded += (_, _) => _previewTimer.Stop();
        RefreshPreview();
    }

    internal void SetVirtualMachines(IEnumerable<StartupVmOption> machines)
    {
        _rows.Clear();
        _vmList.Children.Clear();
        foreach (var vm in machines.Where(vm => Guid.TryParse(vm.Id, out _)).OrderBy(vm => vm.Name))
        {
            string id = Guid.Parse(vm.Id).ToString("D");
            var row = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            row.Children.Add(new TextBlock { Text = vm.Name, Foreground = _foreground, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
            var alias = new TextBox { Text = _initial.Aliases.GetValueOrDefault(id, ""), Margin = new Thickness(0, 4, 0, 4), ToolTip = "为 " + vm.Name + " 指定控制栏显示名称；留空使用统一规则" };
            var preview = new TextBlock { Foreground = _foreground, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
            row.Children.Add(alias);
            row.Children.Add(preview);
            _rows[id] = (vm.Name, alias, preview);
            alias.TextChanged += (_, _) => QueuePreview();
            _vmList.Children.Add(row);
        }
        if (_rows.Count == 0) _vmList.Children.Add(Label("未找到虚拟机。"));
        RefreshPreview();
    }

    internal void ShowLoadError()
    {
        _vmList.Children.Clear();
        _vmList.Children.Add(Label("无法读取虚拟机列表；已有单台显示名称保持不变。"));
    }

    internal ConsoleToolbarNameOptions GetOptions(ConsoleToolbarNameOptions current)
    {
        var result = new ConsoleToolbarNameOptions
        {
            Enabled = _enabled.IsChecked == true,
            Pattern = _pattern.Text,
            Replacement = _replacement.Text,
            Aliases = new Dictionary<string, string>(current.Aliases)
        };
        foreach (var entry in _rows)
        {
            string alias = entry.Value.Alias.Text.Trim();
            if (alias.Length == 0) result.Aliases.Remove(entry.Key);
            else result.Aliases[entry.Key] = alias;
        }
        return result;
    }

    private TextBlock Label(string text) => new() { Text = text, Foreground = _foreground, TextWrapping = TextWrapping.Wrap };
    private void QueuePreview() { _previewTimer.Stop(); _previewTimer.Start(); }

    private void RefreshPreview()
    {
        _body.IsEnabled = _enabled.IsChecked == true;
        var options = GetOptions(_initial);
        options.TryValidate(out string error);
        _error.Text = error;
        _error.Visibility = error.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var entry in _rows)
        {
            string display = options.GetDisplayName(entry.Key, entry.Value.Name, out string note);
            entry.Value.Preview.Text = "显示为：" + display + (note.Length == 0 ? "" : "（" + note + "）");
        }
    }
}
