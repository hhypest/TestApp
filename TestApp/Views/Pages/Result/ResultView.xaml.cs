using System.Windows.Controls;

namespace TestApp.Views.Pages.Result;

public partial class ResultView : Page, IResultView
{
    #region Конструктор

    public ResultView()
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