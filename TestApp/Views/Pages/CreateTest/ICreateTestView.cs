namespace TestApp.Views.Pages.CreateTest;

public interface ICreateTestView : IView
{
    public void NavigationTo<T>(T page);
}