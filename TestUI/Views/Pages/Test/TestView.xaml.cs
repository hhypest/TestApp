using System.Windows.Controls;

namespace TestUI.Views.Pages.Test;

public partial class TestView : Page, ITestView
{
    public TestView()
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