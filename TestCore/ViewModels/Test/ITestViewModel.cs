using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TestCore.ViewModels.Ask;

namespace TestCore.ViewModels.Test;

public interface ITestViewModel
{
    public string TitleTest { get; set; }

    public string PathSave { get; set; }

    public bool IsSaveTest { get; set; }

    public int CountAsk { get; set; }

    public ObservableCollection<IAskViewModel> AsksList { get; set; }

    public IAskViewModel? SelectedAsk { get; set; }

    public IRelayCommand CreateNewTestCommand { get; }

    public IAsyncRelayCommand LoadTestCommand { get; }

    public IAsyncRelayCommand<string?> SaveTestCommand { get; }

    public IAsyncRelayCommand SaveAsTestCommand { get; }

    public IRelayCommand AddNewAskCommand { get; }

    public IRelayCommand<IAskViewModel?> EditAskCommand { get; }

    public IRelayCommand<IAskViewModel?> DeleteAskCommand { get; }

    public IRelayCommand<int?> DeleteAllAsksCommand { get; }
}