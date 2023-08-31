using System.Windows.Controls;

namespace TestUI.Views.Pages.StartUp;

public partial class StartUpView : Page, IStartUpView
{
    public StartUpView()
    {
        InitializeComponent();
    }

    public void SetDataContext<T>(T dataContext)
    {
        DataContext = dataContext;
    }

    public void NavigationTo<T>(T view)
    {
        NavigationService.Navigate(view);
    }
}