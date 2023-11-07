using System.Windows.Controls;

namespace TestApp.Views.Pages.Test;

public partial class TestView : Page, ITestView
{
    #region Конструктор

    public TestView()
    {
        InitializeComponent();
    }

    #endregion Конструктор

    #region Реализация интерфейса

    public void NavigationTo<T>(T page)
    {
        NavigationService.Navigate(page);
    }

    public void SetDataContext<T>(T dataContext)
    {
        DataContext = dataContext;
    }

    #endregion Реализация интерфейса
}