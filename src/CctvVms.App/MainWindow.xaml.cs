using System.Windows;
using CctvVms.App.ViewModels;

namespace CctvVms.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}