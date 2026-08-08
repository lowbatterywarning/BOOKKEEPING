using System.Windows;

namespace Bookkeeping.Wpf.Views;

public partial class ProgramDetailWindow : Window
{
    public ProgramDetailWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ViewModels.ProgramDetailViewModel vm)
                await vm.LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load program details:\n\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
