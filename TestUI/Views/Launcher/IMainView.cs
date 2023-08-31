namespace TestUI.Views.Launcher;

public interface IMainView
{
    public void ShowView();

    public void SetDataContext<T>(T dataContext);

    public void NavigationTo<T>(T view);
}