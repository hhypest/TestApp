using System.Windows;
using TestApp.Extensions;

namespace TestApp.Views.Dialog;

public interface IDialogView
{
    public bool ResultDialog { get; }

    public void CreateView(string title, string message, TypeDialogView typeDialog);

    public void ShowView();

    public void SetOwner(Window owner);
}