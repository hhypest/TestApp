namespace TestApp.Views;

public interface IView
{
    public void SetDataContext<T>(T dataContext);
}