using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.ComponentModel.DataAnnotations;
using TestApp.Messages.AnswerMessages;

namespace TestApp.ViewModels.Answer;

public partial class AnswerViewModel : ObservableValidator, IAnswerViewModel
{
    #region Зависимости
    private readonly IMessenger _messenger;
    #endregion

    #region Свойства модели представления
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptInputAnswerCommand))]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Вариант ответа не может быть пустым!")]
    [MinLength(5, ErrorMessage = "Вариант ответа должен состоять минимум из пяти символов!")]
    private string _titleAnswer;

    private bool _isAnswered;

    public bool IsAnswered { get => _isAnswered; set
        {
            if (!SetProperty(ref _isAnswered, value))
                return;

            _messenger.Send(new CanExecuteMessage(true));
        } }

    public bool IsEditItem { get; set; }
    #endregion

    #region Конструктор
    public AnswerViewModel(IMessenger messenger)
    {
        _messenger = messenger;
        _titleAnswer = "Новый вариант ответа";
    }
    #endregion

    #region Команды
    [RelayCommand(CanExecute = nameof(CanExecuteAcceptInput))]
    private void AcceptInputAnswer()
    {
        if (IsEditItem)
        {
            IsEditItem = false;
            _messenger.Send(new EditAnswerMessage(true));
        }
        else
        {
            _messenger.Send(new CreateAnswerMessage(this));
        }
    }

    [RelayCommand]
    private void CancelInputAnswer()
    {
        if (IsEditItem)
        {
            IsEditItem = false;
            _messenger.Send(new EditAnswerMessage(false));
        }
        else
        {
            _messenger.Send(new CreateAnswerMessage(null));
        }
    }
    #endregion

    #region Предикаты
    private bool CanExecuteAcceptInput()
    {
        return !string.IsNullOrWhiteSpace(TitleAnswer) && TitleAnswer.Length > 4;
    }
    #endregion
}