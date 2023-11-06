using System.Windows;
using System.Windows.Controls;

namespace TestApp.Views.Dialog.DialogContent;

public partial class InformationDialog : UserControl
{
    private Window Owner { get; }

    public InformationDialog(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        Owner.DialogResult = true;
    }
}