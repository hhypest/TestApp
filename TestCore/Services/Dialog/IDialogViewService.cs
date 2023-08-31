namespace TestCore.Services.Dialog;

public interface IDialogViewService
{
    public FileInfo? OpenFileDialog(string titleDialog, string fileFilter = "Все файлы|*.*");

    public FileInfo? SaveFileDialog(string titleDialog, string fileFilter = "Все файлы|*.*", string? fileName = null);

    public void ShowErrorMessage(string titleView, string messageView);

    public void ShowInformationMessage(string titleView, string messageView);

    public bool ShowQuestionMessage(string titleView, string messageView);

    public void ShowWarningMessage(string titleView, string messageView);
}