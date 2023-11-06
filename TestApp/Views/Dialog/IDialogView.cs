namespace TestApp.Views.Dialog;

public enum TypeDialogView : byte
{
    InformationDialog = 0,
    QuestionDialog = 1,
    ErrorDialog = 2
}

public interface IDialogView
{
    public void CreateView(string title, string message, TypeDialogView typeDialog);

    public void ShowView();
}