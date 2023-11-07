namespace TestApp.Views.Test;

public interface ITestView : IView
{
    public void NavigationTo<T>(T page);
}