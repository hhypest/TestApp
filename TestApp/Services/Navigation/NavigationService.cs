using CommunityToolkit.Mvvm.ComponentModel;
using System;
using TestApp.Extensions;
using TestApp.Services.Factory;
using TestApp.ViewModels.Ask;
using TestApp.ViewModels.Launch;
using TestApp.ViewModels.Test;

namespace TestApp.Services.Navigation;

public sealed class NavigationService : INavigationService
{
    #region Зависимости
    private readonly IFactoryService _factoryService;
    #endregion

    #region Конструктор
    public NavigationService(IFactoryService factoryService)
    {
        _factoryService = factoryService;
    }
    #endregion

    #region Реализация интерфейса
    public void StartService()
    {
        var shell = _factoryService.CreateShellView();
        var page = _factoryService.CreateView(NavigationType.Launch);
        var dataContext = _factoryService.CreateViewModel<ILaunchViewModel>(NavigationType.Launch);
        var test = _factoryService.CreateViewModel<ITestViewModel>(NavigationType.Test);
        test.IsSubscribeMessage = true;

        page.SetDataContext(dataContext);
        shell.NavigationTo(page);
        shell.ShowView();
    }

    public void StopService()
    {
        var test = _factoryService.CreateViewModel<ITestViewModel>(NavigationType.Test);
        test.IsSubscribeMessage = false;
    }

    public void NavigationTo(NavigationType type)
    {
        var shell = _factoryService.CreateShellView();
        var page = _factoryService.CreateView(type);
        dynamic dataContext = type switch
        {
            NavigationType.Launch => _factoryService.CreateViewModel<ILaunchViewModel>(NavigationType.Launch),
            NavigationType.Test => _factoryService.CreateViewModel<ITestViewModel>(NavigationType.Test),
            NavigationType.Ask => _factoryService.CreateViewModel<IAskViewModel>(NavigationType.Ask),
            _ => throw new NotSupportedException()
        };

        page.SetDataContext(dataContext);
        shell.NavigationTo(page);
    }

    public void NavigationTo<T>(NavigationType type, T dataContext) where T : class
    {
        var shell = _factoryService.CreateShellView();
        var page = _factoryService.CreateView(type);

        page.SetDataContext(dataContext);
        shell.NavigationTo(page);
    }
    #endregion
}