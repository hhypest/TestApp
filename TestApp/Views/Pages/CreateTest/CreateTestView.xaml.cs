using System.Windows.Controls;

namespace TestApp.Views.Pages.CreateTest;

public partial class CreateTestView : Page, ICreateTestView
{
    #region Конструктор
    public CreateTestView()
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