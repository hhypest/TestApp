using Microsoft.Extensions.DependencyInjection;
using NightModel.Commands;
using NightModel.Services.NavigationViewModel.NavigationContainer;
using NightModel.Services.NavigationViewModel.NavigationService;
using NightModel.ViewModel;
using System;
using System.Windows.Input;
using TestApp.Service.ErrorsMessage;
using TestApp.ViewModel.AppViewModel.Interfaces;

namespace TestApp.ViewModel.AppViewModel;

public class CreateTestViewModel : RelayViewModel, ICreateTestViewModel
{
    private string _titleTest;
    private readonly IServiceProvider _service;
    private readonly IErrorsMessage _errorsMessage;

    public string TitleTest { get => _titleTest; set => Set(ref _titleTest, value, TitleTestValidate, _errorsMessage.GetError(nameof(TitleTest)), nameof(TitleTest)); }

    public ICommand OkDialogResultCommand { get; }

    public ICommand CancelDialogResultCommand { get; }

    private bool TitleTestValidate(string name)
        => string.IsNullOrWhiteSpace(name) || name.Length < 5;

    private void OnOkDialogResultExecute()
    {
        var test = _service.GetRequiredService<ITestViewModel>();
        test.TitleTest = TitleTest;
        test.IsSaveTest = false;
        test.CountAsk = 0;
        test.PathTest = string.Empty;
        test.Asks.Clear();

        var navigationService = new NavigationService<TestViewModel>(_service.GetRequiredService<INavigationContainer>(), () => (TestViewModel)test);
        navigationService.Navigate();
    }

    private void OnCancelDialogResultExecute()
    {
        var startUp = _service.GetRequiredService<IStartUpViewModel>();
        var navigationService = new NavigationService<StartUpViewModel>(_service.GetRequiredService<INavigationContainer>(), () => (StartUpViewModel)startUp);
        navigationService.Navigate();
    }

    public CreateTestViewModel(IServiceProvider service)
    {
        _service = service;
        _errorsMessage = _service.GetRequiredService<IErrorsMessage>();
        _titleTest = "Новый тест";

        OkDialogResultCommand = new RelayCommand(OnOkDialogResultExecute, () => !HasErrors);
        CancelDialogResultCommand = new RelayCommand(OnCancelDialogResultExecute);
    }
}