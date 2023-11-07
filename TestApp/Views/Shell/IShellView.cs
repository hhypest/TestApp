namespace TestApp.Views.Shell;

public interface IShellView : IView
{
    public void ShowView();

    public void NavigationTo<T>(T page);
}