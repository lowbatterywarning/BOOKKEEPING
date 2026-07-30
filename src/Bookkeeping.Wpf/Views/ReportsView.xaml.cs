using System.Windows;
using System.Windows.Controls;

namespace Bookkeeping.Wpf.Views;

public partial class ReportsView : UserControl
{
    public ReportsView() => InitializeComponent();

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ReportsViewModel vm)
            await vm.LoadAsync();
    }
}
