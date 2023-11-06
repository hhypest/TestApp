using System.Windows;
using System.Windows.Controls;

namespace TestApp.Views.Dialog.DialogContent;

public partial class QuestionDialog : UserControl
{
    private Window Owner { get; }

    public QuestionDialog(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        Owner.DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Owner.DialogResult = false;
    }
}