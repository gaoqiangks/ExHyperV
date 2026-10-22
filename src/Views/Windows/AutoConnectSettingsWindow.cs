using System.Windows;
using System.Windows.Controls;
using ExHyperV.Services;
using ExHyperV.Tools;
using System.Windows.Media;
using CheckBox = System.Windows.Controls.CheckBox;

using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
namespace ExHyperV.Views;
internal sealed class AutoConnectSettingsWindow : Window
{
    public AutoConnectSettingsWindow()
    {
        Title = "自动连接窗口与登录设置"; Width = 560; Height = 650;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32,32,32));
        Foreground = System.Windows.Media.Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.CanResize;
        var o = AutoConnectOptions.Load();
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "请在虚拟机管理界面中，为每台虚拟机勾选启动时自动连接。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) });
        var existing = new CheckBox { Content = "已有虚拟机连接时，再次启动打开管理主界面", IsChecked = o.OpenMainWhenConnected, Margin = new Thickness(0,0,0,12) }; panel.Children.Add(existing);
        var vmPage = new CheckBox { Content = "主界面默认打开虚拟机管理", IsChecked = o.DefaultVmManagementPage, Margin = new Thickness(0,0,0,12) }; panel.Children.Add(vmPage);
        var enhanced = new CheckBox { Content = "每次连接优先使用增强会话", IsChecked = o.PreferEnhancedSession, Margin = new Thickness(0,0,0,12) }; panel.Children.Add(enhanced);
        var clipboard = new CheckBox { Content = "增强会话共享剪贴板（复制文字和文件）", IsChecked = o.RedirectClipboard, Margin = new Thickness(0,0,0,12) }; panel.Children.Add(clipboard);
        var drives = new CheckBox { Content = "增强会话共享宿主机磁盘（支持文件复制粘贴）", IsChecked = o.RedirectDrives, Margin = new Thickness(0,0,0,8) }; panel.Children.Add(drives);
        panel.Children.Add(new TextBlock { Text = "文件复制粘贴需要同时启用以上两项。共享磁盘会出现在来宾的“此电脑”中。\n保存后重新连接生效；关闭连接窗口不会关闭虚拟机。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) });
        panel.Children.Add(new TextBlock { Text = "虚拟机连接窗口的控制栏位置" });
        var toolbarPosition = new ComboBox { Margin = new Thickness(0,4,0,8) };
        var toolbarPositions = new[] { "Top", "Left", "Right", "Bottom" };
        foreach (var label in new[] { "顶部", "左侧", "右侧", "底部" }) toolbarPosition.Items.Add(label);
        toolbarPosition.SelectedIndex = Array.IndexOf(toolbarPositions, o.ConsoleToolbarPosition); panel.Children.Add(toolbarPosition);
        panel.Children.Add(new TextBlock { Text = "仅影响虚拟机连接窗口，保存后重新打开连接窗口生效。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) });
        panel.Children.Add(new TextBlock { Text = "虚拟机连接窗口的控制栏背景色" });
        var customBackground = new CheckBox { Content = "使用自定义背景色（关闭时跟随主题）", Foreground = Foreground, IsChecked = !string.IsNullOrEmpty(o.ConsoleToolbarBackground), Margin = new Thickness(0,8,0,8) };
        panel.Children.Add(customBackground);
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,8) };
        var colorText = new TextBox { Text = string.IsNullOrEmpty(o.ConsoleToolbarBackground) ? "#202020" : o.ConsoleToolbarBackground, Width = 115, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 7, ToolTip = "输入 #RRGGBB 格式的颜色" };
        var chooseColor = new Button { Content = "选择颜色…", Margin = new Thickness(8,0,0,0), Padding = new Thickness(12,4,12,4) };
        var previewText = new TextBlock { Text = "控制栏 Aa", Margin = new Thickness(10,4,10,4), VerticalAlignment = VerticalAlignment.Center };
        var preview = new Border { Child = previewText, CornerRadius = new CornerRadius(4), Margin = new Thickness(10,0,0,0), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
        void RefreshPreview()
        {
            colorRow.IsEnabled = customBackground.IsChecked == true;
            if (AutoConnectOptions.TryNormalizeToolbarBackground(colorText.Text, out string color) && color.Length != 0)
            {
                ConsoleToolbarAppearance.Apply(preview, color);
                previewText.Foreground = (System.Windows.Media.Brush)preview.Resources["TextFillColorPrimaryBrush"];
            }
        }
        chooseColor.Click += (_, _) =>
        {
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
            if (AutoConnectOptions.TryNormalizeToolbarBackground(colorText.Text, out string color) && color.Length != 0)
                dialog.Color = System.Drawing.ColorTranslator.FromHtml(color);
            var owner = new System.Windows.Forms.NativeWindow();
            owner.AssignHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            try
            {
                if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
                    colorText.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            }
            finally { owner.ReleaseHandle(); }
        };
        colorText.TextChanged += (_, _) => RefreshPreview();
        customBackground.Checked += (_, _) => RefreshPreview();
        customBackground.Unchecked += (_, _) => RefreshPreview();
        colorRow.Children.Add(colorText); colorRow.Children.Add(chooseColor); colorRow.Children.Add(preview); panel.Children.Add(colorRow);
        RefreshPreview();
        panel.Children.Add(new TextBlock { Text = "可选择颜色或输入色值，例如 #202020。保存后立即应用，文字和图标自动适配明暗。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) });
        var toolbarName = new ConsoleToolbarNameEditor(o.ConsoleToolbarName, Foreground);
        panel.Children.Add(toolbarName);
        panel.Children.Add(new TextBlock { Text = "连接后的窗口状态" });
        var mode = new ComboBox { Margin = new Thickness(0,4,0,12) };
        var values = new[] { "Normal", "Maximized", "Minimized", "FullScreen" };
        foreach (var label in new[] { "普通窗口", "最大化窗口", "最小化", "全屏" }) mode.Items.Add(label);
        mode.SelectedIndex = Math.Max(0, Array.IndexOf(values, o.WindowMode)); panel.Children.Add(mode);
        var close = new CheckBox { Content = "连接成功后关闭管理主窗口", IsChecked = o.CloseMainAfterConnect, Margin = new Thickness(0,0,0,12) }; panel.Children.Add(close);
        var login = new CheckBox { Content = "连接后自动登录 / 解锁无密码账户", IsChecked = o.PasswordlessAutoLogin, Margin = new Thickness(0,0,0,8) }; panel.Children.Add(login);
        panel.Children.Add(new TextBlock { Text = "分别选择需要自动登录的虚拟机：", Margin = new Thickness(16,0,0,6) });
        var loginList = new StackPanel { Margin = new Thickness(16,0,0,8), IsEnabled = login.IsChecked == true };
        var loading = new TextBlock { Text = "正在读取虚拟机…" }; loginList.Children.Add(loading); panel.Children.Add(loginList);
        var vmLoginChecks = new Dictionary<string, CheckBox>();
        login.Checked += (_, _) => loginList.IsEnabled = true;
        login.Unchecked += (_, _) => loginList.IsEnabled = false;
        Loaded += async (_, _) =>
        {
            try
            {
                var result = await WmiApi.QueryAsync("SELECT Name, ElementName FROM Msvm_ComputerSystem",
                    vm => new StartupVmOption { Id = vm["Name"]?.ToString() ?? "", Name = vm["ElementName"]?.ToString() ?? "" }, WmiScope.HyperV);
                if (!result.Success) throw new InvalidOperationException(result.Error);
                toolbarName.SetVirtualMachines(result.Data ?? []);
                loginList.Children.Clear();
                foreach (var vm in (result.Data ?? []).Where(vm => Guid.TryParse(vm.Id, out _)).OrderBy(vm => vm.Name))
                {
                    string id = Guid.Parse(vm.Id).ToString("D");
                    var check = new CheckBox { Content = vm.Name, Foreground = Foreground, IsChecked = o.IsPasswordlessLoginSelected(id), Margin = new Thickness(0,0,0,6) };
                    vmLoginChecks[id] = check; loginList.Children.Add(check);
                }
                if (vmLoginChecks.Count == 0) loginList.Children.Add(new TextBlock { Text = "未找到虚拟机。" });
            }
            catch (Exception ex) { loading.Text = "无法读取虚拟机列表；已有单机设置保持不变。"; toolbarName.ShowLoadError(); AutoConnectLog.Write("Login VM list failed: " + ex.Message); }
        };
        panel.Children.Add(new TextBlock { Text = "总开关与单机开关均开启时才会自动登录，与启动时自动连接独立。\n关闭后停止后续登录操作；开启后在下次连接或重启时生效。\n仅适用于无密码 Windows 账户，每次连接最多发送两次 Enter。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) });
        var save = new Button { Content = "保存", Width = 100, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        save.Click += (_, _) => {
            try
            {
                if (customBackground.IsChecked == true && string.IsNullOrWhiteSpace(colorText.Text))
                    throw new ArgumentException("请输入控制栏背景色，或关闭自定义背景色以跟随主题。");
                var current = AutoConnectOptions.Load();
                current.DefaultVmManagementPage = vmPage.IsChecked == true;
                current.OpenMainWhenConnected = existing.IsChecked == true;
                current.PreferEnhancedSession = enhanced.IsChecked == true;
                current.RedirectClipboard = clipboard.IsChecked == true;
                current.RedirectDrives = drives.IsChecked == true;
                current.ConsoleToolbarPosition = toolbarPositions[toolbarPosition.SelectedIndex];
                current.ConsoleToolbarBackground = customBackground.IsChecked == true ? colorText.Text : "";
                current.ConsoleToolbarName = toolbarName.GetOptions(current.ConsoleToolbarName);
                current.WindowMode = values[mode.SelectedIndex];
                current.CloseMainAfterConnect = close.IsChecked == true;
                current.PasswordlessAutoLogin = login.IsChecked == true;
                if (vmLoginChecks.Count > 0)
                {
                    current.PasswordlessAutoLoginByVm ??= new();
                    foreach (var vm in vmLoginChecks) current.PasswordlessAutoLoginByVm[vm.Key] = vm.Value.IsChecked == true;
                }
                current.Save();
                Close();
            }
            catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "保存失败"); }
        };
        foreach (var checkBox in panel.Children.OfType<CheckBox>()) checkBox.Foreground = Foreground;
        panel.Children.Add(save); Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
