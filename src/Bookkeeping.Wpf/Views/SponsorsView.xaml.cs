using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Bookkeeping.Wpf.Views;

public partial class SponsorsView : UserControl
{
    public SponsorsView() => InitializeComponent();

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ViewModels.SponsorsViewModel vm)
                await vm.LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Sponsors load error:\n\n{ex}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.SponsorsViewModel vm && vm.SelectedSponsor != null)
            vm.EditCommand.Execute(null);
    }
}
