using System.Windows;
using System.Windows.Controls;

namespace Bookkeeping.Wpf.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ViewModels.DashboardViewModel vm)
                await vm.LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Dashboard load error:\n\n{ex}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is ViewModels.DashboardViewModel vm)
            vm.TileColumns = ComputeTileColumns(e.NewSize.Width);
    }

    /// <summary>
    /// Computes how many tile columns fit in the given width. Each tile has a
    /// minimum width of 280px plus an 8px right margin.
    /// </summary>
    private static int ComputeTileColumns(double width)
    {
        const double minTileWidth = 280 + 8; // MinWidth + margin
        if (width <= 0 || double.IsNaN(width)) return 3;
        var columns = (int)Math.Floor(width / minTileWidth);
        return Math.Clamp(columns, 1, 4);
    }
}
