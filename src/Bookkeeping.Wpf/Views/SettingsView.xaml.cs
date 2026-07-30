using System.Windows;
using System.Windows.Controls;

namespace Bookkeeping.Wpf.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SettingsViewModel vm)
            await vm.LoadAsync();
    }
}
