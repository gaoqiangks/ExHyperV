using ExHyperV.ViewModels;

namespace ExHyperV.Views
{
    public partial class SettingsPage
    {
        private void OpenAutoConnectSettings(object sender, System.Windows.RoutedEventArgs e) => new AutoConnectSettingsWindow { Owner = System.Windows.Window.GetWindow(this) }.ShowDialog();
        public SettingsPage()
        {
            InitializeComponent();
            DataContext = new SettingsPageViewModel();
        }
    }
}