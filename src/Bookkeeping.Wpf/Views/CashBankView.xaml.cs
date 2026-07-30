using System.Windows;
using System.Windows.Controls;

namespace Bookkeeping.Wpf.Views;

public partial class CashBankView : UserControl
{
    public CashBankView() => InitializeComponent();

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.CashBankViewModel vm)
            await vm.LoadAsync();
    }
}
