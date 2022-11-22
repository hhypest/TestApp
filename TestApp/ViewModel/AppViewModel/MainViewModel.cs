using Microsoft.Extensions.DependencyInjection;
using NightModel.Services.NavigationViewModel.NavigationContainer;
using NightModel.Services.NavigationViewModel.NavigationService;
using NightModel.ViewModel;
using System;
using TestApp.ViewModel.AppViewModel.Interfaces;

namespace TestApp.ViewModel.AppViewModel;

public class MainViewModel : RelayViewModel, IMainViewModel
{
    private RelayViewModel? _currentViewModel;
    private readonly IServiceProvider _service;
    private readonly INavigationContainer _container;

    public RelayViewModel? CurrentViewModel { get => _currentViewModel; set => Set(ref _currentViewModel, value); }

    private void OnViewModelChanged()
        => CurrentViewModel = _container.ViewModel;

    public MainViewModel(IServiceProvider service)
    {
        _service = service;
        _container = _service.GetRequiredService<INavigationContainer>();
        _container.ViewModelChanged += OnViewModelChanged;

        var startUp = _service.GetRequiredService<IStartUpViewModel>();
        var navigationService = new NavigationService<StartUpViewModel>(_container, () => (StartUpViewModel)startUp);
        navigationService.Navigate();
    }
}