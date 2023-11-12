using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TestApp.ViewModels.Ask;

namespace TestApp.ViewModels.Result;

public interface IResultViewModel
{
    public string ResultTest { get; set; }

    public ObservableCollection<IAskViewModel> AsksList { get; set; }

    public bool IsVisibleExpander { get; set; }

    public IAskViewModel? SelectedAsk { get; set; }

    public IRelayCommand CompleteResultCommand { get; }
}