using System.Windows;
using TestApp.ViewModel.AppViewModel;
using TestApp.ViewModel.AppViewModel.Interfaces;

namespace TestApp.View;

public partial class Shell : Window
{
    public Shell(IMainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = (MainViewModel)viewModel;
    }
}