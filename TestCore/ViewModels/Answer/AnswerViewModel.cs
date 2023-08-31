using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace TestCore.ViewModels.Answer;

public partial class AnswerViewModel : ObservableValidator, IAnswerViewModel
{
    #region Свойства модели представления

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Поле обязательно для заполнения!")]
    [MinLength(5, ErrorMessage = "Вариант ответа должен состоять миниму из пяти символов!")]
    private string _optionAnswer;

    [ObservableProperty]
    private bool _isAnswered;

    public bool IsEditItem { get; set; }

    #endregion Свойства модели представления

    #region Конструктор

    public AnswerViewModel()
    {
        _optionAnswer = "Новый вариант ответа";
    }

    #endregion Конструктор
}