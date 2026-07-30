using System.Windows;
using System.Windows.Controls;

namespace Bookkeeping.Wpf.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ViewModels.DashboardViewModel vm)
                await vm.LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Dashboard load error:\n\n{ex}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
