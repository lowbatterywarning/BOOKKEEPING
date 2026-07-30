using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Bookkeeping.Wpf.Views;

public partial class ProgramsView : UserControl
{
    public ProgramsView() => InitializeComponent();

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ProgramsViewModel vm)
            await vm.LoadAsync();
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.ProgramsViewModel vm && vm.SelectedProgram != null)
            vm.EditCommand.Execute(null);
    }
}
