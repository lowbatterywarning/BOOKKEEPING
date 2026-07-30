using System.Windows;
using Bookkeeping.Wpf.ViewModels;

namespace Bookkeeping.Wpf.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += (_, _) =>
        {
            viewModel.Settings.ConfigureAutoBackup();
        };
    }
}
