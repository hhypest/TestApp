using System.Windows;

namespace TestUI.Views.Dialogs.Errors;

public interface IErrorsWindow
{
    public bool ResultShowView { get; }

    public void ShowView(string title, string message);

    public void SetOwnerView(Window? owner);
}