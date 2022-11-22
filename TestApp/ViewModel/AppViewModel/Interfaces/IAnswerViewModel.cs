namespace TestApp.ViewModel.AppViewModel.Interfaces;

public interface IAnswerViewModel
{
    public string Option { get; set; }

    public bool IsAnswered { get; set; }

    public bool HasError { get; }
}