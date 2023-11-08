using CommunityToolkit.Mvvm.Input;

namespace TestApp.ViewModels.Answer;

public interface IAnswerViewModel
{
    public string TitleAnswer { get; set; }

    public bool IsAnswered { get; set; }

    public bool IsEditItem { get; set; }

    public IRelayCommand AcceptInputAnswerCommand { get; }

    public IRelayCommand CancelInputAnswerCommand { get; }
}