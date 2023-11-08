namespace TestApp.Services.Dialog;

public interface IDialogService
{
    public void ShowMessage(string title, string message);

    public bool ShowQuestion(string title, string message);
}