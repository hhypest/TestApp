using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TestApp.ViewModels.Ask;

namespace TestApp.ViewModels.Test;

public interface ITestViewModel
{
    public string TitleTest { get; set; }

    public string PathSaveTest { get; set; }

    public bool IsSaveTest { get; set; }

    public int CountAsk {  get; set; }

    public ObservableCollection<IAskViewModel> AsksList { get; set; }

    public IAskViewModel? SelectedAsk { get; set; }

    public bool IsSubscribeMessage { set; }

    public IRelayCommand CreateNewTestCommand { get; }

    public IAsyncRelayCommand LoadTestCommand { get; }

    public IAsyncRelayCommand<string?> SaveTestCommand { get; }

    public IAsyncRelayCommand SaveTestAsCommand { get; }

    public IRelayCommand AddNewAskCommand { get; }

    public IRelayCommand<IAskViewModel?> EditSelectedAskCommand { get; }

    public IRelayCommand<IAskViewModel?> DeleteSelectedAskCommand { get; }

    public IRelayCommand DeleteAllAskCommand { get; }
}