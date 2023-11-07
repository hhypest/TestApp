using System.Windows.Controls;

namespace TestApp.Views.Pages.Launch;

public partial class LaunchView : Page, ILaunchView
{
    #region Конструктор
    public LaunchView()
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