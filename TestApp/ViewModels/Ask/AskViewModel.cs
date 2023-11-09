using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using TestApp.Extensions;
using TestApp.Messages.AnswerMessages;
using TestApp.Messages.AskMessages;
using TestApp.Services.Factory;
using TestApp.ViewModels.Answer;

namespace TestApp.ViewModels.Ask;

public partial class AskViewModel : ObservableValidator, IAskViewModel, IRecipient<CreateAnswerMessage>, IRecipient<EditAnswerMessage>, IRecipient<CanExecuteMessage>
{
    #region Зависимости

    private readonly IMessenger _messenger;
    private readonly IFactoryService _factoryService;

    #endregion Зависимости

    #region Свойства модели представления

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptInputAskCommand))]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Вопрос не может быть пустым!")]
    [MinLength(5, ErrorMessage = "Вопрос должен состоять минимум из пяти символов!")]
    private string _titleAsk;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptInputAskCommand))]
    private bool _isSingleAnswer;

    [ObservableProperty]
    private bool _isOpenDialog;

    [ObservableProperty]
    private ObservableCollection<IAnswerViewModel> _answersList;

    [ObservableProperty]
    private IAnswerViewModel? _selectedAnswer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptInputAskCommand), nameof(DeleteAllAnswerCommand))]
    private int _countAnswer;

    public bool IsEditItem { get; set; }

    public EditAskItem? EditAsk { get; set; }

    private EditAnswerItem? EditAnswer { get; set; }

    public bool IsSubcribeMessage
    {
        set
        {
            if (value)
            {
                _messenger.RegisterAll(this);
                if (!IsEditItem)
                    return;

                EditAsk = new EditAskItem(TitleAsk, IsSingleAnswer, AnswersList.Select(answer => new EditAnswerItem(answer.TitleAnswer, answer.IsAnswered)).ToList());
            }
            else
            {
                _messenger.UnregisterAll(this);
            }
        }
    }

    #endregion Свойства модели представления

    #region Обработка сообщений

    public void Receive(CreateAnswerMessage message)
    {
        IsOpenDialog = false;

        if (message.Value is null)
        {
            SelectedAnswer = null;
        }
        else
        {
            AnswersList.Add(message.Value);
            CountAnswer++;
        }
    }

    public void Receive(EditAnswerMessage message)
    {
        SelectedAnswer!.IsEditItem = false;
        IsOpenDialog = false;

        if (message.Value)
            return;

        SelectedAnswer.TitleAnswer = EditAnswer!.Value.TitleAnswer;
        SelectedAnswer.IsAnswered = EditAnswer.Value.IsAnswered;
        SelectedAnswer.IsEditItem = false;
        EditAnswer = null;
    }

    public void Receive(CanExecuteMessage message)
    {
        AcceptInputAskCommand.NotifyCanExecuteChanged();
    }

    #endregion Обработка сообщений

    #region Конструктор

    public AskViewModel(IMessenger messenger, IFactoryService factoryService)
    {
        _messenger = messenger;
        _factoryService = factoryService;
        _titleAsk = "Новый вопрос";
        _answersList = new();
    }

    #endregion Конструктор

    #region Команды

    [RelayCommand]
    private void AddNewAnswer()
    {
        IsOpenDialog = true;
        SelectedAnswer = _factoryService.CreateViewModel<IAnswerViewModel>(NavigationType.Answer);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteChangeAnswer))]
    private void EditSelectedAnswer(IAnswerViewModel? answer)
    {
        EditAnswer = new EditAnswerItem(answer!.TitleAnswer, answer.IsAnswered);
        SelectedAnswer!.IsEditItem = true;
        IsOpenDialog = true;
    }

    [RelayCommand(CanExecute = nameof(CanExecuteChangeAnswer))]
    private void DeleteSelectedAnswer(IAnswerViewModel? answer)
    {
        AnswersList.Remove(answer!);
        CountAnswer--;
    }

    [RelayCommand(CanExecute = nameof(CanExecuteDeleteAllAnswer))]
    private void DeleteAllAnswer()
    {
        AnswersList.Clear();
        CountAnswer = 0;
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAcceptInputAsk))]
    private void AcceptInputAsk()
    {
        if (IsEditItem)
        {
            IsEditItem = false;
            IsSubcribeMessage = false;
            EditAsk = null;
            _messenger.Send(new EditAskMessage(true));
        }
        else
        {
            IsSubcribeMessage = false;
            _messenger.Send(new CreateAskMessage(this));
        }
    }

    [RelayCommand]
    private void CancelInputAsk()
    {
        if (IsEditItem)
        {
            IsEditItem = false;
            IsSubcribeMessage = false;
            _messenger.Send(new EditAskMessage(false));
        }
        else
        {
            IsSubcribeMessage = false;
            _messenger.Send(new CreateAskMessage(null));
        }
    }

    #endregion Команды

    #region Предикаты

    private bool CanExecuteChangeAnswer(IAnswerViewModel? answer)
    {
        return answer is not null;
    }

    private bool CanExecuteDeleteAllAnswer()
    {
        return CountAnswer > 0;
    }

    private bool CanExecuteAcceptInputAsk()
    {
        if (string.IsNullOrWhiteSpace(TitleAsk) && TitleAsk.Length < 5)
            return false;

        if (CountAnswer < 1)
            return false;

        if (IsSingleAnswer && AnswersList.Count(answer => answer.IsAnswered) > 1)
            return false;

        return true;
    }

    #endregion Предикаты
}