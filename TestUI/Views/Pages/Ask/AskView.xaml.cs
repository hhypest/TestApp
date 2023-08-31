using System.Windows.Controls;

namespace TestUI.Views.Pages.Ask;

public partial class AskView : Page, IAskView
{
    public AskView()
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