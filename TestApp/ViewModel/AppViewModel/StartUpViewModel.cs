using DataModel;
using DataModel.DataRecord;
using Microsoft.Extensions.DependencyInjection;
using NightModel.AsyncCommands;
using NightModel.Commands;
using NightModel.Services.DialogMessageService;
using NightModel.Services.NavigationViewModel.NavigationContainer;
using NightModel.Services.NavigationViewModel.NavigationService;
using NightModel.ViewModel;
using System;
using System.Threading.Tasks;
using System.Windows.Input;
using TestApp.Extansions;
using TestApp.ViewModel.AppViewModel.Interfaces;

namespace TestApp.ViewModel.AppViewModel;

public class StartUpViewModel : RelayViewModel, IStartUpViewModel
{
    private readonly IServiceProvider _service;

    public ICommand CreateNewTestCommand { get; }

    public ICommand OpenTestCommand { get; }

    private void OnCreateNewTestExecute()
    {
        var createTest = _service.GetRequiredService<ICreateTestViewModel>();
        var navigationService = new NavigationService<CreateTestViewModel>(_service.GetRequiredService<INavigationContainer>(), () => (CreateTestViewModel)createTest);
        navigationService.Navigate();
    }

    private async Task OnOpenTestExecute()
    {
        var dialog = _service.GetRequiredService<IDialogMessage>();
        var fileInfo = dialog.OpenFileDialog("Какой тест открыть?", "Файл теста|*.json");
        if (fileInfo is null)
            return;

        try
        {
            var result = await DataExtansion<TestModel>.OpenTestAsync(fileInfo.FullName);
            if (result.Asks is null)
                throw new InvalidOperationException($"Открываемый файл <{fileInfo.Name}> - не является тестом!");

            var test = _service.GetRequiredService<ITestViewModel>();
            var map = result.DataModelMap(_service);
            test.TitleTest = map.TitleTest;
            test.Asks = map.Asks;
            test.CountAsk = map.CountAsk;
            test.PathTest = fileInfo.FullName;
            test.IsSaveTest = true;

            var navigationService = new NavigationService<TestViewModel>(_service.GetRequiredService<INavigationContainer>(), () => (TestViewModel)test);
            navigationService.Navigate();
        }
        catch (Exception ex)
        {
            dialog.ShowErrorMessage("Произошла ошибка!", ex.Message);
        }
    }

    public StartUpViewModel(IServiceProvider service)
    {
        _service = service;
        CreateNewTestCommand = new RelayCommand(OnCreateNewTestExecute);
        OpenTestCommand = new RelayAsyncCommand(OnOpenTestExecute);
    }
}