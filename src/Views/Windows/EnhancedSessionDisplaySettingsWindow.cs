using System.Windows;
using System.Windows.Controls;
using ExHyperV.Models;
using ExHyperV.Services;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;

namespace ExHyperV.Views;

internal sealed class EnhancedSessionDisplaySettingsWindow : Window
{
    public EnhancedSessionDisplaySettingsWindow()
    {
        Title = DesktopScaleText.Title;
        Width = 540; Height = 370; MinWidth = 440; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32));
        Foreground = System.Windows.Media.Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = DesktopScaleText.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new TextBlock { Text = DesktopScaleText.VirtualMachine });
        var machines = new ComboBox { DisplayMemberPath = nameof(VmInstance.Name), Margin = new Thickness(0, 6, 0, 14) };
        panel.Children.Add(machines);
        panel.Children.Add(new TextBlock { Text = DesktopScaleText.Title });
        var scale = new ComboBox { DisplayMemberPath = nameof(DesktopScaleOption.Label), Margin = new Thickness(0, 6, 0, 12) };
        panel.Children.Add(scale);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(hint);
        var save = new Button { Content = DesktopScaleText.Save, Width = 110, IsEnabled = false, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        panel.Children.Add(save);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        machines.SelectionChanged += (_, _) =>
        {
            if (machines.SelectedItem is not VmInstance vm) return;
            bool supported = !EnhancedSessionCredentials.IsConfigured(vm.Id.ToString());
            int current = EnhancedSessionDisplayOptions.Load().GetPercent(vm.Id.ToString());
            var choices = EnhancedSessionDisplayOptions.Presets.Select(p => new DesktopScaleOption(p)).ToList();
            if (!choices.Any(p => p.Percent == current)) choices.Add(new(current));
            scale.ItemsSource = choices;
            scale.SelectedItem = choices.First(p => p.Percent == current);
            scale.IsEnabled = save.IsEnabled = supported;
            hint.Text = supported ? DesktopScaleText.Hint : DesktopScaleText.LinuxHint;
        };
        save.Click += (_, _) =>
        {
            if (machines.SelectedItem is not VmInstance vm || scale.SelectedItem is not DesktopScaleOption choice) return;
            try
            {
                var options = EnhancedSessionDisplayOptions.Load();
                options.SetPercent(vm.Id.ToString(), choice.Percent);
                options.Save(vm.Id.ToString());
                Close();
            }
            catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, DesktopScaleText.SaveFailed); }
        };
        Loaded += async (_, _) =>
        {
            try
            {
                var vms = await new VmQueryService().GetVmListAsync();
                machines.ItemsSource = vms;
                machines.SelectedItem = vms.FirstOrDefault(v => !EnhancedSessionCredentials.IsConfigured(v.Id.ToString())) ?? vms.FirstOrDefault();
            }
            catch (Exception ex) { hint.Text = ex.Message; }
        };
    }
}
