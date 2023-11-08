using TestApp.Extensions;

namespace TestApp.Services.Navigation;

public interface INavigationService
{
    public void StartService();

    public void StopService();

    public void NavigationTo(NavigationType type);

    public void NavigationTo<T>(NavigationType type, T dataContext) where T : class;
}