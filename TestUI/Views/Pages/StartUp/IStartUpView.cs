namespace TestUI.Views.Pages.StartUp;

public interface IStartUpView : IView
{
    public void NavigationTo<T>(T view);
}