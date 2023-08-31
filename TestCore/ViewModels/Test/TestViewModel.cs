using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using TestCore.Extensions;
using TestCore.Messages;
using TestCore.Services.Dialog;
using TestCore.Services.Navigation;
using TestCore.ViewModels.Ask;
using TestCore.ViewModels.StartUp;
using TestData.Extensions;

namespace TestCore.ViewModels.Test;

public partial class TestViewModel : ObservableValidator, ITestViewModel, IRecipient<TitleTestMessage>, IRecipient<AddItemMessage>, IRecipient<CancelAddItemMessage>, IRecipient<EditItemMessage>
{
    #region Зависимости

    private readonly IDialogViewService _dialogService;
    private readonly INavigationService _navigationService;

    #endregion Зависимости

    #region Свойства модели представления

    [ObservableProperty]
    private string _titleTest;

    [ObservableProperty]
    private string _pathSave;

    [ObservableProperty]
    private bool _isSaveTest;

    [ObservableProperty]
    private int _countAsk;

    [ObservableProperty]
    private ObservableCollection<IAskViewModel> _asksList;

    [ObservableProperty]
    private IAskViewModel? _selectedAsk;

    private EditAsk? _editItem;

    #endregion Свойства модели представления

    #region Конструктор

    public TestViewModel(IDialogViewService dialogService, INavigationService navigationService)
    {
        _titleTest = string.Empty;
        _pathSave = string.Empty;
        _asksList = new();
        _dialogService = dialogService;
        _navigationService = navigationService;

        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    #endregion Конструктор

    #region Получение сообщений

    public void Receive(TitleTestMessage message)
    {
        TitleTest = message.Value;
        CountAsk = 0;
        IsSaveTest = false;
        PathSave = string.Empty;
        AsksList.Clear();
        SelectedAsk = null;
    }

    public void Receive(AddItemMessage message)
    {
        AsksList.Add(message.Value);
        CountAsk++;
        IsSaveTest = false;
        _navigationService.NavigationTo(typeof(ITestViewModel));
    }

    public void Receive(CancelAddItemMessage message)
    {
        _navigationService.NavigationTo(typeof(ITestViewModel));
    }

    public void Receive(EditItemMessage message)
    {
        if (!message.Value)
        {
            SelectedAsk!.QuestionAsk = _editItem!.Value.QuestionAsk;
            SelectedAsk.IsSingleAnswer = _editItem.Value.IsSingleAnswer;
            SelectedAsk.AnswersList = new(_editItem.Value.AnswersList.Select(answer =>
            {
                var newAnswer = _navigationService.GetAnswerInstance();
                newAnswer.OptionAnswer = answer.OptionAnswer;
                newAnswer.IsAnswered = answer.IsAnswered;

                return newAnswer;
            }));
        }

        _navigationService.NavigationTo(typeof(ITestViewModel));
    }

    #endregion Получение сообщений

    #region Команды

    [RelayCommand]
    private void OnCreateNewTest()
    {
        if (IsSaveTest)
        {
            _navigationService.NavigationTo(typeof(IStartUpViewModel));
            return;
        }

        if (!_dialogService.ShowQuestionMessage("Внимание", $"Тест <{TitleTest}> - не сохранен. Вы хотите продолжить?"))
            return;

        _navigationService.NavigationTo(typeof(IStartUpViewModel));
    }

    [RelayCommand]
    private async Task OnLoadTest()
    {
        if (!IsSaveTest && !_dialogService.ShowQuestionMessage("Внимание", $"Тест <{TitleTest}> - не сохранен. Вы хотите продолжить?"))
            return;

        var file = _dialogService.OpenFileDialog("Какой тест открыть?", "Файл теста|*.test");
        if (file is null)
            return;

        try
        {
            var test = await SerializeExtension.LoadJson(file.FullName);
            IsSaveTest = true;
            CountAsk = test.AsksList.Count;
            PathSave = file.FullName;
            AsksList.Clear();

            test.ToViewModel(this, _navigationService);
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorMessage("Внимание!", $"Произошла ошибка!\n{ex.Message}\n{ex.Source}");
        }
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteSaveTest))]
    private async Task OnSaveTest(string? path)
    {
        var test = this.ToModel();

        try
        {
            await test.SaveJson(path!);
            IsSaveTest = true;
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorMessage("Внимание!", $"Произошла ошибка!\n{ex.Message}\n{ex.Source}\n{nameof(test)}");
        }
    }

    [RelayCommand]
    private async Task OnSaveAsTest()
    {
        var file = _dialogService.SaveFileDialog($"Куда сохранить тест - <{TitleTest}>", "Файл теста|*.test", TitleTest);
        if (file is null)
            return;

        var test = this.ToModel();

        try
        {
            await test.SaveJson(file.FullName);
            IsSaveTest = true;
            PathSave = file.FullName;
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorMessage("Внимание!", $"Произошла ошибка!\n{ex.Message}\n{ex.Source}\n{nameof(test)}");
        }
    }

    [RelayCommand]
    private void OnAddNewAsk()
    {
        _navigationService.NavigationTo(typeof(IAskViewModel));
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteActionAsk))]
    private void OnEditAsk(IAskViewModel? ask)
    {
        IsSaveTest = false;
        ask!.IsEditItem = true;
        _editItem = new(ask!.QuestionAsk, ask.IsSingleAnswer, ask.AnswersList.Select(answer => new EditAnswer(answer.OptionAnswer, answer.IsAnswered)).ToList());
        _navigationService.NavigationTo(typeof(IAskViewModel), ask);
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteActionAsk))]
    private void OnDeleteAsk(IAskViewModel? ask)
    {
        AsksList.Remove(ask!);
        CountAsk--;
        IsSaveTest = false;
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteDelete))]
    private void OnDeleteAllAsks(int? count)
    {
        if (!_dialogService.ShowQuestionMessage("Операция удаления", "Вы действительно хотите удалить все вопрососы?"))
            return;

        AsksList.Clear();
        CountAsk = 0;
        IsSaveTest = false;
    }

    #endregion Команды

    #region Предикаты

    private bool OnCanExecuteSaveTest(string? path)
    {
        return !string.IsNullOrWhiteSpace(path);
    }

    private bool OnCanExecuteActionAsk(IAskViewModel? ask)
    {
        return ask is not null;
    }

    private bool OnCanExecuteDelete(int? count)
    {
        return count > 0;
    }

    #endregion Предикаты
}