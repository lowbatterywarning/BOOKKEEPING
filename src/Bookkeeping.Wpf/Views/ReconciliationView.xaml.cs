using System.Windows;
using System.Windows.Controls;

namespace Bookkeeping.Wpf.Views;

public partial class ReconciliationView : UserControl
{
    public ReconciliationView() => InitializeComponent();

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ReconciliationViewModel vm)
            await vm.LoadAsync();
    }
}
