using System.Windows.Controls;

namespace TestApp.Views.Pages.Ask;

public partial class AskView : Page, IAskView
{
    #region Конструктор
    public AskView()
    {
        InitializeComponent();
    }
    #endregion

    #region Реализация интерфейса
    public void NavigationTo<T>(T page)
    {
        NavigationService.Navigate(page);
    }

    public void SetDataContext<T>(T dataContext)
    {
        DataContext = dataContext;
    }
    #endregion
}