using System.Collections.ObjectModel;
using System.Linq;
using TestApp.ViewModel.AppViewModel.Interfaces;

namespace TestApp.Extansions;

internal static class ObservableEx
{
    internal static bool CheckAnswers(this ObservableCollection<IAnswerViewModel> collection)
        => collection.Any(answer => answer.IsAnswered);

    internal static bool CheckErrors(this ObservableCollection<IAnswerViewModel> collection)
        => collection.Any(answer => answer.HasError);
}