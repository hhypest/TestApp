using System.Windows;
using System.Windows.Controls;

namespace TestApp.View.AppView;

public partial class AskView : UserControl
{
    public AskView()
    {
        InitializeComponent();
    }

    private void OnStyleChanged(object sender, RoutedEventArgs e)
    {
        var selector = AnswerListView.ItemContainerStyleSelector;
        AnswerListView.ItemContainerStyleSelector = null;
        AnswerListView.ItemContainerStyleSelector = selector;
    }
}