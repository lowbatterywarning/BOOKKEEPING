using System.Windows;
using System.Windows.Controls;

namespace Bookkeeping.Wpf.Views;

public partial class IncomeView : UserControl
{
    public IncomeView() => InitializeComponent();

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.IncomeViewModel vm)
            await vm.LoadAsync();
    }
}
