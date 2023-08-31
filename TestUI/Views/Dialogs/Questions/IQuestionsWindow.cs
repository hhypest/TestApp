using System.Windows;

namespace TestUI.Views.Dialogs.Questions;

public interface IQuestionsWindow
{
    public bool ResultShowView { get; }

    public void ShowView(string title, string message);

    public void SetOwnerView(Window? owner);
}