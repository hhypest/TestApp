using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TestCore.ViewModels.Answer;

namespace TestCore.ViewModels.Ask;

public interface IAskViewModel
{
    public string QuestionAsk { get; set; }

    public bool IsOpenAnswerDialog { get; set; }

    public bool IsSingleAnswer { get; set; }

    public int CountAnswer { get; set; }

    public ObservableCollection<IAnswerViewModel> AnswersList { get; set; }

    public IAnswerViewModel? SelectedAnswer { get; set; }

    public bool IsEditItem { get; set; }

    public IRelayCommand AddNewAnswerCommand { get; }

    public IRelayCommand<IAnswerViewModel?> EditAnswerCommand { get; }

    public IRelayCommand<IAnswerViewModel?> DeleteAnswerCommand { get; }

    public IRelayCommand<int?> DeleteAllAnswersCommand { get; }

    public IRelayCommand<string?> OkDialogAnswerCommand { get; }

    public IRelayCommand CancelDialogAnswerCommand { get; }

    public IRelayCommand OkAskViewCommand { get; }

    public IRelayCommand CancelAskViewCommand { get; }
}