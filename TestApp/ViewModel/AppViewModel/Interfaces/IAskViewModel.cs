using System.Collections.ObjectModel;

namespace TestApp.ViewModel.AppViewModel.Interfaces;

public interface IAskViewModel
{
    public string Question { get; set; }

    public bool IsSingleAnswers { get; set; }

    public IAnswerViewModel? SelectedAnswer { get; set; }

    public ObservableCollection<IAnswerViewModel> Answers { get; set; }

    public void BeginAddAsk();

    public void BeginEditAsk();
}