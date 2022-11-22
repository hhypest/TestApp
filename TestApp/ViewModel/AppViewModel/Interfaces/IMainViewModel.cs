using NightModel.ViewModel;

namespace TestApp.ViewModel.AppViewModel.Interfaces;

public interface IMainViewModel
{
    public RelayViewModel? CurrentViewModel { get; set; }
}