using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TestApp.Extensions;
using TestApp.ViewModels.Answer;

namespace TestApp.ViewModels.Ask;

public interface IAskViewModel
{
    public string TitleAsk { get; set; }

    public bool IsSingleAnswer { get; set; }

    public bool IsOpenDialog { get; set; }

    public ObservableCollection<IAnswerViewModel> AnswersList { get; set; }

    public IAnswerViewModel? SelectedAnswer { get; set; }

    public int CountAnswer { get; set; }

    public bool IsEditItem { get; set; }

    public EditAskItem? EditAsk { get; set; }

    public bool IsSubcribeMessage { set; }

    public IRelayCommand AddNewAnswerCommand { get; }

    public IRelayCommand<IAnswerViewModel?> EditSelectedAnswerCommand { get; }

    public IRelayCommand<IAnswerViewModel?> DeleteSelectedAnswerCommand { get; }

    public IRelayCommand DeleteAllAnswerCommand { get; }

    public IRelayCommand AcceptInputAskCommand { get; }

    public IRelayCommand CancelInputAskCommand { get; }
}