namespace TestApp.Views.Pages.Result;

public interface IResultView : IView
{
    public void NavigationTo<T>(T page);
}