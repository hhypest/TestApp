using TestCore.ViewModels.Answer;
using TestCore.ViewModels.Ask;
using TestCore.ViewModels.Test;

namespace TestCore.Services.Navigation;

public interface INavigationService
{
    public void NavigationTo(Type viewModelType);

    public void NavigationTo<T>(Type viewModelType, T viewModel);

    public ITestViewModel GetTestInstance();

    public IAskViewModel GetAskInstance();

    public IAnswerViewModel GetAnswerInstance();
}