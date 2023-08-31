using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using TestCore.Extensions;
using TestCore.Messages;
using TestCore.Services.Dialog;
using TestCore.ViewModels.Answer;

namespace TestCore.ViewModels.Ask;

public partial class AskViewModel : ObservableValidator, IAskViewModel
{
    #region Зависимости
    private readonly IDialogViewService _dialogService;
    #endregion

    #region Свойства модели представления
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkAskViewCommand))]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Поле обязательно для заполнения!")]
    [MinLength(5, ErrorMessage = "Вопрос должен состоять минимум из пяти символов!")]
    private string _questionAsk;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkAskViewCommand))]
    private bool _isOpenAnswerDialog;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkAskViewCommand))]
    private bool _isSingleAnswer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkAskViewCommand))]
    private int _countAnswer;

    [ObservableProperty]
    private ObservableCollection<IAnswerViewModel> _answersList;

    [ObservableProperty]
    private IAnswerViewModel? _selectedAnswer;

    private EditAnswer? _editItem;

    public bool IsEditItem { get; set; }
    #endregion

    #region Конструктор
    public AskViewModel(IDialogViewService dialogService)
    {
        _questionAsk = "Новый вопрос";
        _answersList = new();
        _dialogService = dialogService;
    }
    #endregion

    #region Команды
    [RelayCommand]
    private void OnAddNewAnswer()
    {
        SelectedAnswer = new AnswerViewModel();
        IsOpenAnswerDialog = true;
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteActionAnswer))]
    private void OnEditAnswer(IAnswerViewModel? answer)
    {
        IsOpenAnswerDialog = true;
        answer!.IsEditItem = true;
        _editItem = new(answer.OptionAnswer, answer.IsAnswered);
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteActionAnswer))]
    private void OnDeleteAnswer(IAnswerViewModel? answer)
    {
        AnswersList.Remove(answer!);
        CountAnswer--;
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteCountAnswer))]
    private void OnDeleteAllAnswers(int? count)
    {
        if (!_dialogService.ShowQuestionMessage("Операция удаления", "Вы действительно хотите удалить все варианты ответа?"))
            return;

        AnswersList.Clear();
        CountAnswer = 0;
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteOkDialog))]
    private void OnOkDialogAnswer(string? answer)
    {
        if (SelectedAnswer!.IsEditItem)
        {
            IsOpenAnswerDialog = false;
            SelectedAnswer.IsEditItem = false;
            return;
        }

        AnswersList.Add(SelectedAnswer!);
        CountAnswer++;
        IsOpenAnswerDialog = false;
    }

    [RelayCommand]
    private void OnCancelDialogAnswer()
    {
        if (!SelectedAnswer!.IsEditItem)
        {
            SelectedAnswer = null;
            IsOpenAnswerDialog = false;
            return;
        }

        IsOpenAnswerDialog = false;
        SelectedAnswer.OptionAnswer = _editItem!.Value.OptionAnswer;
        SelectedAnswer.IsAnswered = _editItem!.Value.IsAnswered;
        _editItem = null;
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteView))]
    private void OnOkAskView()
    {
        switch (IsEditItem)
        {
            case true:
                IsEditItem = false;
                WeakReferenceMessenger.Default.Send(new EditItemMessage(true));
                break;
            case false:
                WeakReferenceMessenger.Default.Send(new AddItemMessage(this));
                break;
        }
    }

    [RelayCommand]
    private void OnCancelAskView()
    {
        switch (IsEditItem)
        {
            case true:
                IsEditItem = false;
                WeakReferenceMessenger.Default.Send(new EditItemMessage(false));
                break;
            case false:
                WeakReferenceMessenger.Default.Send(new CancelAddItemMessage(false));
                break;
        }
    }
    #endregion

    #region Предикаты
    private bool OnCanExecuteActionAnswer(IAnswerViewModel? answer)
    {
        return answer is not null;
    }

    private bool OnCanExecuteCountAnswer(int? count)
    {
        return count > 0;
    }

    private bool OnCanExecuteOkDialog(string? answer)
    {
        if (answer is null)
            return false;

        if (string.IsNullOrWhiteSpace(answer) || answer.Length < 5)
            return false;

        return true;
    }

    private bool OnCanExecuteView()
    {
        if (CountAnswer < 1)
            return false;

        if (IsSingleAnswer && AnswersList.Count(answer => answer.IsAnswered) > 1)
            return false;

        if (string.IsNullOrWhiteSpace(QuestionAsk) || QuestionAsk.Length < 5)
            return false;

        return true;
    }
    #endregion
}