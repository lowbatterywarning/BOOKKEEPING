using System.Windows;
using System.Windows.Controls;

namespace Bookkeeping.Wpf.Views;

public partial class ExpensesView : UserControl
{
    public ExpensesView() => InitializeComponent();

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ExpensesViewModel vm)
            await vm.LoadAsync();
    }
}
