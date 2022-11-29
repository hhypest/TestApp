using Microsoft.Extensions.DependencyInjection;
using NightModel.Commands;
using NightModel.Services.NavigationViewModel.NavigationContainer;
using NightModel.Services.NavigationViewModel.NavigationService;
using NightModel.ViewModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TestApp.Extensions;
using TestApp.Service.ErrorsMessage;
using TestApp.ViewModel.AppViewModel.Interfaces;
using TestApp.ViewModel.AppViewModel.Structure;

namespace TestApp.ViewModel.AppViewModel;

public class AskViewModel : RelayViewModel, IAskViewModel
{
    #region Закрытые поля

    private string _question;
    private bool _isSingleAnswers;
    private IAnswerViewModel? _selectedAnswer;
    private ObservableCollection<IAnswerViewModel> _answers;
    private readonly IServiceProvider _service;
    private readonly IErrorsMessage _errorsMessage;

    #endregion Закрытые поля

    #region Свойства

    public string Question { get => _question; set => Set(ref _question, value, QuestionValidate, _errorsMessage.GetError(nameof(Question)), nameof(Question)); }

    public bool IsSingleAnswers { get => _isSingleAnswers; set => Set(ref _isSingleAnswers, value); }

    public IAnswerViewModel? SelectedAnswer { get => _selectedAnswer; set => Set(ref _selectedAnswer, value); }

    public ObservableCollection<IAnswerViewModel> Answers { get => _answers; set => Set(ref _answers, value); }

    private bool IsNew { get; set; }

    private IEditableAsk? Editable { get; set; }

    #endregion Свойства

    #region Команды

    public ICommand AddNewAnswerCommand { get; }

    public ICommand RemoveSelectedAnswerCommand { get; }

    public ICommand RemoveAllAnswerCommand { get; }

    public ICommand OkDialogResultCommand { get; }

    public ICommand CancelDialogResultCommand { get; }

    #endregion Команды

    #region Предикат

    private bool QuestionValidate(string question)
        => string.IsNullOrWhiteSpace(question) || question.Length < 5;

    #endregion Предикат

    #region Методы команд

    private void OnAddNewAnswerExecute()
        => Answers.Add(_service.GetRequiredService<IAnswerViewModel>());

    private void OnRemoveSelectedAnswerExecute(IAnswerViewModel? selectedAnswer)
    {
        if (selectedAnswer is null)
            return;

        Answers.Remove(selectedAnswer);
    }

    private void OnRemoveAllAnswerExecute()
        => Answers.Clear();

    private void OnOkDialogResultExecute()
    {
        var test = _service.GetRequiredService<ITestViewModel>();
        var navigationService = new NavigationService<TestViewModel>(_service.GetRequiredService<INavigationContainer>(), () => (TestViewModel)test);

        if (IsNew)
        {
            test.Asks.Add(this);
            test.IsSaveTest = false;
            IsNew = false;
            test.CountAsk++;
            navigationService.Navigate();
        }
        else
        {
            test.IsSaveTest = false;
            navigationService.Navigate();
        }
    }

    private bool OnCanOkDialogResultExecute()
    {
        if (Answers.Count < 1)
            return false;

        if (!Answers.CheckAnswers())
            return false;

        if (Answers.CheckErrors())
            return false;

        if (HasErrors)
            return false;

        return true;
    }

    private void OnCancelDialogResultExecute()
    {
        if (!IsNew)
            EndEditAsk();

        var test = _service.GetRequiredService<ITestViewModel>();
        var navigationService = new NavigationService<TestViewModel>(_service.GetRequiredService<INavigationContainer>(), () => (TestViewModel)test);
        navigationService.Navigate();
    }

    #endregion Методы команд

    #region Методы начала\отмены редактирования

    public void BeginAddAsk()
        => IsNew = true;

    public void BeginEditAsk()
        => Editable = new EditableAsk(this);

    private void EndEditAsk()
    {
        if (Editable is null)
            return;

        Question = Editable.Question;
        IsSingleAnswers = Editable.IsSingleAnswers;
        Answers = new ObservableCollection<IAnswerViewModel>(Editable.Answers.Select(answer => new AnswerViewModel(_errorsMessage)
        {
            Option = answer.Option,
            IsAnswered = answer.IsAnswered,
        }));

        Editable = null;
    }

    #endregion Методы начала\отмены редактирования

    #region Конструктор

    public AskViewModel(IServiceProvider service)
    {
        _service = service;
        _errorsMessage = _service.GetRequiredService<IErrorsMessage>();
        _question = "Новый вопрос";
        _answers = new();

        AddNewAnswerCommand = new RelayCommand(OnAddNewAnswerExecute);
        RemoveSelectedAnswerCommand = new RelayCommand<IAnswerViewModel>(OnRemoveSelectedAnswerExecute, answer => answer is not null);
        RemoveAllAnswerCommand = new RelayCommand(OnRemoveAllAnswerExecute, () => Answers.Count > 0);
        OkDialogResultCommand = new RelayCommand(OnOkDialogResultExecute, OnCanOkDialogResultExecute);
        CancelDialogResultCommand = new RelayCommand(OnCancelDialogResultExecute);
    }

    #endregion Конструктор
}