namespace TestUI.Views.Pages.Ask;

public interface IAskView : IView
{
    public void NavigationTo<T>(T view);
}