using System.Windows;

namespace TestUI.Views.Dialogs.Warnings;

public interface IWarningsWindow
{
    public bool ResultShowView { get; }

    public void ShowView(string title, string message);

    public void SetOwnerView(Window? owner);
}