using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Bookkeeping.Wpf.Views;

public partial class SearchView : UserControl
{
    public SearchView() => InitializeComponent();

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SearchViewModel vm)
        {
            try { await vm.SearchAsync(); }
            catch (Exception ex) { vm.StatusMessage = $"Error: {ex.Message}"; }
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SearchButton_Click(sender, e);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SearchViewModel vm)
            vm.ClearSearch();
    }
}
