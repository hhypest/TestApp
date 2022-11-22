using System.Windows.Input;

namespace TestApp.ViewModel.AppViewModel.Interfaces;

public interface IStartUpViewModel
{
    public ICommand CreateNewTestCommand { get; }

    public ICommand OpenTestCommand { get; }
}