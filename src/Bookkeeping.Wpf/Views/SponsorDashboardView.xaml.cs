using System.Windows;
using System.Windows.Controls;

namespace Bookkeeping.Wpf.Views;

public partial class SponsorDashboardView : UserControl
{
    public SponsorDashboardView()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ViewModels.SponsorDashboardViewModel vm)
                await vm.LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Sponsor dashboard load error:\n\n{ex}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
