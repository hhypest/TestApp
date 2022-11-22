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
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TestApp.Extansions;
using TestApp.Service.ErrorsMessage;
using TestApp.ViewModel.AppViewModel.Interfaces;

namespace TestApp.ViewModel.AppViewModel;

public class TestViewModel : RelayViewModel, ITestViewModel
{
    #region Закрытые поля

    private string _titleTest;
    private string _pathTest;
    private int _countAsk;
    private bool _isSaveTest;
    private IAskViewModel? _selectedAsk;
    private ObservableCollection<IAskViewModel> _asks;
    private readonly IServiceProvider _service;
    private readonly IErrorsMessage _errorsMessage;
    private readonly IDialogMessage _dialogMessage;

    #endregion Закрытые поля

    #region Свойства

    public string TitleTest { get => _titleTest; set => Set(ref _titleTest, value, TitleValidate, _errorsMessage.GetError(nameof(TitleTest)), nameof(TitleTest)); }

    public string PathTest { get => _pathTest; set => Set(ref _pathTest, value); }

    public int CountAsk { get => _countAsk; set => Set(ref _countAsk, value); }

    public bool IsSaveTest { get => _isSaveTest; set => Set(ref _isSaveTest, value); }

    public IAskViewModel? SelectedAsk { get => _selectedAsk; set => Set(ref _selectedAsk, value); }

    public ObservableCollection<IAskViewModel> Asks { get => _asks; set => Set(ref _asks, value); }

    #endregion Свойства

    #region Команды

    public ICommand CreateNewTestCommand { get; }

    public ICommand OpenTestCommand { get; }

    public ICommand SaveTestCommand { get; }

    public ICommand SaveAsTestCommand { get; }

    public ICommand CloseAppCommand { get; }

    public ICommand AddNewAskCommand { get; }

    public ICommand EditSelectedAskCommand { get; }

    public ICommand DeleteSelectedAskCommand { get; }

    public ICommand DeleteAllAskCommand { get; }

    #endregion Команды

    #region Предикат

    private bool TitleValidate(string title)
        => string.IsNullOrWhiteSpace(title) || title.Length < 5;

    #endregion Предикат

    #region Методы команд

    private void OnCreateNewTestExecute(bool? save)
    {
        if (save != true && !_dialogMessage.ShowQuestionDialog("Данные не сохранены!",
                                                               $"Тест <{TitleTest}> не сохранен, при продолжении данные будут потеряны. Вы хотите продолжить?"))
        {
            return;
        }

        var createTest = _service.GetRequiredService<ICreateTestViewModel>();
        var navigationService = new NavigationService<CreateTestViewModel>(_service.GetRequiredService<INavigationContainer>(), () => (CreateTestViewModel)createTest);
        navigationService.Navigate();
    }

    private async Task OnOpenTestExecute(bool? save)
    {
        if (save != true && !_dialogMessage.ShowQuestionDialog("Данные не сохранены!",
                                                       $"Тест <{TitleTest}> не сохранен, при продолжении данные будут потеряны. Вы хотите продолжить?"))
        {
            return;
        }

        var fileInfo = _dialogMessage.OpenFileDialog("Какой тест открыть?", "Файл теста|*.json");
        if (fileInfo is null)
            return;

        try
        {
            PathTest = fileInfo.FullName;
            var result = await DataExtansion<TestModel>.OpenTestAsync(PathTest);
            if (result.Asks is null)
                throw new InvalidOperationException($"Открываемый файл <{fileInfo.Name}> - не является тестом!");

            var test = result.DataModelMap(_service);
            TitleTest = test.TitleTest;
            Asks = test.Asks;
            CountAsk = test.CountAsk;
            IsSaveTest = true;
        }
        catch (Exception ex)
        {
            _dialogMessage.ShowErrorMessage("Произошла ошибка!", ex.Message);
            IsSaveTest = false;
        }
    }

    private async Task OnSaveTestExecute(string? path)
    {
        try
        {
            await DataExtansion<TestModel>.SaveTestAsync(this.ViewModelMap(), path!);
            IsSaveTest = true;
        }
        catch (Exception ex)
        {
            _dialogMessage.ShowErrorMessage("Произошла ошибка!", ex.Message);
            IsSaveTest = false;
        }
    }

    private async Task OnSaveAsTestExecute(int? count)
    {
        var fileInfo = _dialogMessage.SaveFileDialog($"Куда сохранить тест <{TitleTest}>", "Файл теста|*.json", TitleTest);
        if (fileInfo is null)
            return;

        PathTest = fileInfo.FullName;

        try
        {
            await DataExtansion<TestModel>.SaveTestAsync(this.ViewModelMap(), PathTest);
            IsSaveTest = true;
        }
        catch (Exception ex)
        {
            _dialogMessage.ShowErrorMessage("Произошла ошибка!", ex.Message);
            IsSaveTest = false;
        }
    }

    private void OnAddNewAskExecute()
    {
        var ask = _service.GetRequiredService<IAskViewModel>();
        ask.BeginAddAsk();
        var navigationService = new NavigationService<AskViewModel>(_service.GetRequiredService<INavigationContainer>(), () => (AskViewModel)ask);
        navigationService.Navigate();
    }

    private void OnEditSelectedAskExecute(IAskViewModel? selectedAsk)
    {
        if (selectedAsk is null)
            return;

        selectedAsk.BeginEditAsk();
        var navigationService = new NavigationService<AskViewModel>(_service.GetRequiredService<INavigationContainer>(), () => (AskViewModel)selectedAsk);
        navigationService.Navigate();
    }

    private void OnDeleteSelectedAskExecute(IAskViewModel? selectedAsk)
    {
        if (selectedAsk is null)
            return;

        Asks.Remove(selectedAsk);
        IsSaveTest = false;
        CountAsk--;
    }

    private void OnDeleteAllAskExecute()
    {
        Asks.Clear();
        IsSaveTest = false;
        CountAsk = 0;
    }

    #endregion Методы команд

    #region Конструктор

    public TestViewModel(IServiceProvider service)
    {
        #region Начальная инициализация

        _titleTest = "Новый тест";
        _pathTest = string.Empty;
        _asks = new();
        _service = service;
        _errorsMessage = _service.GetRequiredService<IErrorsMessage>();
        _dialogMessage = _service.GetRequiredService<IDialogMessage>();

        #endregion Начальная инициализация

        #region Команды "Файл"

        CreateNewTestCommand = new RelayCommand<bool?>(OnCreateNewTestExecute);
        OpenTestCommand = new RelayAsyncCommand<bool?>(OnOpenTestExecute);
        SaveTestCommand = new RelayAsyncCommand<string>(OnSaveTestExecute, path => !string.IsNullOrWhiteSpace(path) && CountAsk > 0);
        SaveAsTestCommand = new RelayAsyncCommand<int?>(OnSaveAsTestExecute, count => count > 0);
        CloseAppCommand = new RelayCommand(Application.Current.Shutdown);

        #endregion Команды "Файл"

        #region Команды "Правка"

        AddNewAskCommand = new RelayCommand(OnAddNewAskExecute);
        EditSelectedAskCommand = new RelayCommand<IAskViewModel>(OnEditSelectedAskExecute, ask => ask is not null);
        DeleteSelectedAskCommand = new RelayCommand<IAskViewModel>(OnDeleteSelectedAskExecute, ask => ask is not null);
        DeleteAllAskCommand = new RelayCommand(OnDeleteAllAskExecute, () => CountAsk > 0);

        #endregion Команды "Правка"
    }

    #endregion Конструктор
}