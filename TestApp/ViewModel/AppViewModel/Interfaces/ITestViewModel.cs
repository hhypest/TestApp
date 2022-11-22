using System.Collections.ObjectModel;

namespace TestApp.ViewModel.AppViewModel.Interfaces;

public interface ITestViewModel
{
    public string TitleTest { get; set; }

    public string PathTest { get; set; }

    public int CountAsk { get; set; }

    public bool IsSaveTest { get; set; }

    public IAskViewModel? SelectedAsk { get; set; }

    public ObservableCollection<IAskViewModel> Asks { get; set; }
}