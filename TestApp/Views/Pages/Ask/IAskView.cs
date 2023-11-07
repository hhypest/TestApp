namespace TestApp.Views.Pages.Ask;

public interface IAskView : IView
{
    public void NavigationTo<T>(T page);
}