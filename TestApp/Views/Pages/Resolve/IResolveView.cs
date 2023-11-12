namespace TestApp.Views.Pages.Resolve;

public interface IResolveView : IView
{
    public void NavigationTo<T>(T page);
}