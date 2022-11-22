using NightModel.ViewModel;
using TestApp.Service.ErrorsMessage;
using TestApp.ViewModel.AppViewModel.Interfaces;

namespace TestApp.ViewModel.AppViewModel;

public class AnswerViewModel : RelayViewModel, IAnswerViewModel
{
    #region Закрытые поля

    private string _option;
    private bool _isAnswered;
    private readonly IErrorsMessage _errorsMessage;

    #endregion Закрытые поля

    #region Свойства

    public string Option { get => _option; set => Set(ref _option, value, OptionValidate, _errorsMessage.GetError(nameof(Option)), nameof(Option)); }

    public bool IsAnswered { get => _isAnswered; set => Set(ref _isAnswered, value); }

    public bool HasError => HasErrors;

    #endregion Свойства

    #region Предикат

    private bool OptionValidate(string option)
        => string.IsNullOrWhiteSpace(option) || option.Length < 5;

    #endregion Предикат

    #region Конструктор

    public AnswerViewModel(IErrorsMessage errorsMessage)
    {
        _errorsMessage = errorsMessage;
        _option = "Новый вариант ответа";
    }

    #endregion Конструктор
}