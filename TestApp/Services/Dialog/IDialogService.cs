using TestApp.Views.Dialog;

namespace TestApp.Services.Dialog;

public interface IDialogService
{
    public void ShowMessage(string title, string message, TypeDialogView typeDialog);

    public bool ShowQuestion(string title, string message, TypeDialogView typeDialog);
}