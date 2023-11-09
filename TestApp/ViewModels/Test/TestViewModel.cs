using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TestApp.Extensions;
using TestApp.Messages.AnswerMessages;
using TestApp.Messages.AskMessages;
using TestApp.Messages.TestMessages;
using TestApp.Services.Dialog;
using TestApp.Services.Factory;
using TestApp.Services.Navigation;
using TestApp.ViewModels.Answer;
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
    [NotifyCanExecuteChangedFor(nameof(DeleteAllAskCommand))]
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
        if (message.Value)
        {
            _navigationService.NavigationTo(NavigationType.Test);
            IsSaveTest = false;
            return;
        }

        SelectedAsk!.TitleAsk = SelectedAsk.EditAsk!.Value.TitleAsk;
        SelectedAsk.IsSingleAnswer = SelectedAsk.EditAsk.Value.IsSingleAnswer;
        SelectedAsk.AnswersList = new(SelectedAsk.EditAsk.Value.AnswerList.Select(answerItem =>
        {
            var answer = _factoryService.CreateViewModel<IAnswerViewModel>(NavigationType.Answer);
            answer.TitleAnswer = answerItem.TitleAnswer;
            answer.IsAnswered = answerItem.IsAnswered;

            return answer;
        }));
        SelectedAsk.CountAnswer = SelectedAsk.AnswersList.Count;
        SelectedAsk.EditAsk = null;

        _navigationService.NavigationTo(NavigationType.Test);
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
    private async Task LoadTest()
    {
        if (!IsSaveTest && !_dialogService.ShowQuestion("Внимание!", $"Тест <{TitleTest}> не сохранен. Продолжить?"))
            return;

        var filePath = _dialogService.LoadFileDialog("Файл теста|*.json");
        if (filePath is null)
            return;

        try
        {
            var test = await DataUtils.LoadTestFile(filePath.FullName);
            this.GetTestViewModel(test, _factoryService, filePath.FullName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при открытии файла");
            _dialogService.ShowMessage("Ошибка", $"Ошибка при открытии файла\n{ex.Message}\n{ex.StackTrace}\n{ex.Source}");
        }
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteSaveTest))]
    private async Task SaveTest(string? path)
    {
        var test = this.GetTestModel();

        try
        {
            await test.SaveTestFile(path!);
            IsSaveTest = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при сохранении файла");
            _dialogService.ShowMessage("Ошибка", $"Ошибка при сохранении файла\n{ex.Message}\n{ex.StackTrace}\n{ex.Source}");
        }
    }

    [RelayCommand]
    private async Task SaveTestAs()
    {
        var filePath = _dialogService.SaveFileDialog("Файл теста|*.json", TitleTest);
        if (filePath is null)
            return;

        PathSaveTest = filePath.FullName;
        var test = this.GetTestModel();

        try
        {
            await test.SaveTestFile(PathSaveTest);
            IsSaveTest = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при сохранении файла");
            _dialogService.ShowMessage("Ошибка", $"Ошибка при сохранении файла\n{ex.Message}\n{ex.StackTrace}\n{ex.Source}");
        }
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
        ask!.IsEditItem = true;
        ask.IsSubcribeMessage = true;
        _navigationService.NavigationTo(NavigationType.Ask, ask);
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