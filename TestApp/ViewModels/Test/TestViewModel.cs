using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TestApp.Extensions;
using TestApp.Messages.AskMessages;
using TestApp.Messages.TestMessages;
using TestApp.Services.Dialog;
using TestApp.Services.Factory;
using TestApp.Services.Navigation;
using TestApp.ViewModels.Ask;

namespace TestApp.ViewModels.Test;

public partial class TestViewModel : ObservableRecipient, ITestViewModel, IRecipient<CreateTestMessage>, IRecipient<CreateAskMessage>, IRecipient<EditAskMessage>
{
    #region Зависимости
    private readonly ILogger<TestViewModel> _logger;
    private readonly IFactoryService _factoryService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    #endregion

    #region Свойства модели представления
    [ObservableProperty]
    private string _titleTest;

    [ObservableProperty]
    private string _pathSaveTest;

    [ObservableProperty]
    private bool _isSaveTest;

    [ObservableProperty]
    private int _countAsk;

    [ObservableProperty]
    private ObservableCollection<IAskViewModel> _asksList;

    [ObservableProperty]
    private IAskViewModel? _selectedAsk;

    public bool IsSubscribeMessage
    {
        set
        {
            IsActive = value;
        }
    }
    #endregion

    #region Обработка сообщений
    public void Receive(CreateTestMessage message)
    {
        TitleTest = message.Value;
        PathSaveTest = string.Empty;
        IsSaveTest = false;
        CountAsk = 0;
        AsksList.Clear();

        _navigationService.NavigationTo(NavigationType.Test);
    }

    public void Receive(CreateAskMessage message)
    {
        if (message.Value is null)
        {
            _navigationService.NavigationTo(NavigationType.Test);
            return;
        }

        AsksList.Add(message.Value);
        CountAsk++;
        IsSaveTest = false;
        _navigationService.NavigationTo(NavigationType.Test);
    }

    public void Receive(EditAskMessage message)
    {
        throw new NotImplementedException();
    }
    #endregion

    #region Коструктор
    public TestViewModel(IMessenger messenger, ILogger<TestViewModel> logger, IFactoryService factoryService, INavigationService navigationService, IDialogService dialogService)
        : base(messenger)
    {
        _logger = logger;
        _factoryService = factoryService;
        _navigationService = navigationService;
        _dialogService = dialogService;

        _titleTest = string.Empty;
        _pathSaveTest = string.Empty;
        _asksList = new();
    }
    #endregion

    #region Команды
    [RelayCommand]
    private void CreateNewTest()
    {
        if (!IsSaveTest && !_dialogService.ShowQuestion("Внимание!", $"Тест <{TitleTest}> не сохранен. Продолжить?"))
            return;

        _navigationService.NavigationTo(NavigationType.Launch);
    }

    [RelayCommand]
    private Task LoadTest()
    {
        throw new NotImplementedException();
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteSaveTest))]
    private Task SaveTest(string? path)
    {
        throw new NotImplementedException();
    }

    [RelayCommand]
    private Task SaveTestAs()
    {
        throw new NotImplementedException();
    }

    [RelayCommand]
    private void AddNewAsk()
    {
        var ask = _factoryService.CreateViewModel<IAskViewModel>(NavigationType.Ask);
        ask.IsSubcribeMessage = true;
        _navigationService.NavigationTo(NavigationType.Ask, ask);
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteChangeAsk))]
    private void EditSelectedAsk(IAskViewModel? ask)
    {
        throw new NotImplementedException();
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteChangeAsk))]
    private void DeleteSelectedAsk(IAskViewModel? ask)
    {
        AsksList.Remove(ask!);
        CountAsk--;
        IsSaveTest = false;
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteDeleteAll))]
    private void DeleteAllAsk()
    {
        AsksList.Clear();
        CountAsk = 0;
        IsSaveTest = false;
    }
    #endregion

    #region Предикаты
    private bool OnCanExecuteSaveTest(string? path)
    {
        return !string.IsNullOrWhiteSpace(path);
    }

    private bool OnCanExecuteChangeAsk(IAskViewModel? ask)
    {
        return ask is not null;
    }

    private bool OnCanExecuteDeleteAll()
    {
        return CountAsk > 0;
    }
    #endregion
}