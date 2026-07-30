using System.Windows;
using System.Windows.Controls;

namespace Bookkeeping.Wpf.Views;

public partial class BudgetView : UserControl
{
    public BudgetView() => InitializeComponent();

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.BudgetViewModel vm)
            await vm.LoadAsync();
    }
}
