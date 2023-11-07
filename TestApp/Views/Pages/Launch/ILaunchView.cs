namespace TestApp.Views.Pages.Launch;

public interface ILaunchView : IView
{
    public void NavigationTo<T>(T page);
}