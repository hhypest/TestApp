namespace TestCore.ViewModels.Answer;

public interface IAnswerViewModel
{
    public string OptionAnswer { get; set; }

    public bool IsAnswered { get; set; }

    public bool IsEditItem { get; set; }
}