using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using TestCore.Services.Navigation;
using TestCore.ViewModels.Answer;
using TestCore.ViewModels.Ask;
using TestCore.ViewModels.StartUp;
using TestCore.ViewModels.Test;
using TestUI.Views;
using TestUI.Views.Launcher;
using TestUI.Views.Pages.Ask;
using TestUI.Views.Pages.StartUp;
using TestUI.Views.Pages.Test;

namespace TestUI.Services.Navigation;

public sealed class NavigationService : INavigationService
{
    #region Зависимости
    private readonly IMainView _mainView;
    private readonly IServiceProvider _services;
    #endregion

    #region Закрытые поля
    private readonly Dictionary<Type, Func<IServiceProvider, ObservableValidator>> _viewModelsStack;
    private readonly Dictionary<Type, Func<IServiceProvider, IView>> _viewsStack;
    #endregion

    #region Конструктор
    public NavigationService(IMainView mainView, IServiceProvider services)
    {
        _mainView = mainView;
        _services = services;

        _viewModelsStack = new()
        {
            [typeof(IStartUpViewModel)] = (service) => (ObservableValidator)ActivatorUtilities.GetServiceOrCreateInstance<IStartUpViewModel>(service),
            [typeof(ITestViewModel)] = (service) => (ObservableValidator)ActivatorUtilities.GetServiceOrCreateInstance<ITestViewModel>(service),
            [typeof(IAskViewModel)] = (service) => (ObservableValidator)ActivatorUtilities.GetServiceOrCreateInstance<IAskViewModel>(service),
            [typeof(IAnswerViewModel)] = (service) => (ObservableValidator)ActivatorUtilities.GetServiceOrCreateInstance<IAnswerViewModel>(service)
        };

        _viewsStack = new()
        {
            [typeof(IStartUpViewModel)] = ActivatorUtilities.GetServiceOrCreateInstance<IStartUpView>,
            [typeof(ITestViewModel)] = ActivatorUtilities.GetServiceOrCreateInstance<ITestView>,
            [typeof(IAskViewModel)] = ActivatorUtilities.GetServiceOrCreateInstance<IAskView>
        };
    }
    #endregion

    #region Реализация интерфейса
    public void NavigationTo(Type viewModelType)
    {
        if (!_viewModelsStack.TryGetValue(viewModelType, out var viewModel))
            throw new InvalidOperationException($"Невозможно получить эелемент в словаре по типу - {viewModelType}!");

        if (!_viewsStack.TryGetValue(viewModelType, out var view))
            throw new InvalidOperationException($"Невозможно получить эелемент в словаре по типу - {viewModelType}!");

        var dataContext = viewModel(_services);
        var page = view(_services);
        page.SetDataContext(dataContext);
        _mainView.NavigationTo(page);
    }

    public void NavigationTo<T>(Type viewModelType, T viewModel)
    {
        if (!_viewsStack.TryGetValue(viewModelType, out var view))
            throw new InvalidOperationException($"Невозможно получить эелемент в словаре по типу - {viewModelType}!");

        var page = view(_services);
        page.SetDataContext(viewModel);
        _mainView.NavigationTo(page);
    }

    public IAnswerViewModel GetAnswerInstance()
    {
        return ActivatorUtilities.GetServiceOrCreateInstance<IAnswerViewModel>(_services);
    }

    public IAskViewModel GetAskInstance()
    {
        return ActivatorUtilities.GetServiceOrCreateInstance<IAskViewModel>(_services);
    }

    public ITestViewModel GetTestInstance()
    {
        return ActivatorUtilities.GetServiceOrCreateInstance<ITestViewModel>(_services);
    }
    #endregion
}