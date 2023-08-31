namespace TestUI.Views.Pages.Test;

public interface ITestView : IView
{
    public void NavigationTo<T>(T view);
}