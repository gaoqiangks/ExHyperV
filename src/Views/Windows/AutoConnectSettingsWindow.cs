using System.Windows;
using System.Windows.Controls;
using ExHyperV.Services;
using CheckBox = System.Windows.Controls.CheckBox;

using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
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
        panel.Children.Add(new TextBlock { Text = "连接后的窗口状态" });
        var mode = new ComboBox { Margin = new Thickness(0,4,0,12) };
        var values = new[] { "Normal", "Maximized", "Minimized", "FullScreen" };
        foreach (var label in new[] { "普通窗口", "最大化窗口", "最小化", "全屏" }) mode.Items.Add(label);
        mode.SelectedIndex = Math.Max(0, Array.IndexOf(values, o.WindowMode)); panel.Children.Add(mode);
        var close = new CheckBox { Content = "连接成功后关闭管理主窗口", IsChecked = o.CloseMainAfterConnect, Margin = new Thickness(0,0,0,12) }; panel.Children.Add(close);
        var login = new CheckBox { Content = "连接后自动登录 / 解锁无密码账户", IsChecked = o.PasswordlessAutoLogin, Margin = new Thickness(0,0,0,8) }; panel.Children.Add(login);
        panel.Children.Add(new TextBlock { Text = "仅适用于当前选中的无密码 Windows 账户。\n连接后发送至多两次 Enter；有密码时仍需手动登录。\n不修改来宾系统的账户或安全策略。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) });
        var save = new Button { Content = "保存", Width = 100, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        save.Click += (_, _) => {
            try { o.DefaultVmManagementPage = vmPage.IsChecked == true; o.OpenMainWhenConnected = existing.IsChecked == true; o.PreferEnhancedSession = enhanced.IsChecked == true; o.WindowMode = values[mode.SelectedIndex]; o.CloseMainAfterConnect = close.IsChecked == true; o.PasswordlessAutoLogin = login.IsChecked == true; o.Save(); Close(); }
            catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "保存失败"); }
        };
        panel.Children.Add(save); Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}