using Microsoft.Extensions.DependencyInjection;
using System;
using TestApp.Extensions;
using TestApp.ViewModels.Answer;
using TestApp.ViewModels.Ask;
using TestApp.ViewModels.Launch;
using TestApp.ViewModels.Test;
using TestApp.Views;
using TestApp.Views.Pages.Ask;
using TestApp.Views.Pages.Launch;
using TestApp.Views.Pages.Test;
using TestApp.Views.Shell;

namespace TestApp.Services.Factory;

public sealed class FactoryService : IFactoryService
{
    #region Зависимости
    private readonly IServiceProvider _services;
    #endregion

    #region Конструктор
    public FactoryService(IServiceProvider services)
    {
        _services = services;
    }
    #endregion

    #region Реализация интерфейса
    public IShellView CreateShellView()
    {
        return ActivatorUtilities.GetServiceOrCreateInstance<IShellView>(_services);
    }

    public IView CreateView(NavigationType typeView)
    {
        return typeView switch
        {
            NavigationType.Shell => ActivatorUtilities.GetServiceOrCreateInstance<IShellView>(_services),
            NavigationType.Launch => ActivatorUtilities.GetServiceOrCreateInstance<ILaunchView>(_services),
            NavigationType.Test => ActivatorUtilities.GetServiceOrCreateInstance<ITestView>(_services),
            NavigationType.Ask => ActivatorUtilities.GetServiceOrCreateInstance<IAskView>(_services),
            _ => throw new NotSupportedException()
        };
    }

    public T CreateViewModel<T>(NavigationType type) where T : class
    {
        return type switch
        {
            NavigationType.Launch => (T)ActivatorUtilities.GetServiceOrCreateInstance<ILaunchViewModel>(_services),
            NavigationType.Test => (T)ActivatorUtilities.GetServiceOrCreateInstance<ITestViewModel>(_services),
            NavigationType.Ask => (T)ActivatorUtilities.GetServiceOrCreateInstance<IAskViewModel>(_services),
            NavigationType.Answer => (T)ActivatorUtilities.GetServiceOrCreateInstance<IAnswerViewModel>(_services),
            _ => throw new NotSupportedException()
        };
    }
    #endregion
}