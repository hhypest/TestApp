using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TestApp.ViewModels.Answer;

namespace TestApp.ViewModels.Resolve;

public interface IResolveViewModel
{
    public string TitleAsk { get; set; }

    public int CountAsks { get; set; }

    public int CurrentCountAsks { get; set; }

    public bool IsSingleAnswered { get; set; }

    public ObservableCollection<IAnswerViewModel> AnswersList { get; set; }

    public bool IsSubscribeMessage { set; }

    public IRelayCommand RelayNextAskCommand { get; }
}