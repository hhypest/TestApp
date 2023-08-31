using System.Windows;

namespace TestUI.Views.Dialogs.Informations;

public interface IInformationsWindow
{
    public bool ResultShowView { get; }

    public void ShowView(string title, string message);

    public void SetOwnerView(Window? owner);
}