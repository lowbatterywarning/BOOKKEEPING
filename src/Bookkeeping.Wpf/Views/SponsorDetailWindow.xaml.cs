using System.Windows;

namespace Bookkeeping.Wpf.Views;

public partial class SponsorDetailWindow : Window
{
    public SponsorDetailWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ViewModels.SponsorDetailViewModel vm)
                await vm.LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load sponsor details:\n\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
