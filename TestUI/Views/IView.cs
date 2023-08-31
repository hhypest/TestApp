namespace TestUI.Views;

public interface IView
{
    public void SetDataContext<T>(T dataContext);
}