using System.Windows.Controls;

namespace TestApp.Views.Pages.Resolve;

public partial class ResolveView : Page, IResolveView
{
    #region Конструктор
    public ResolveView()
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