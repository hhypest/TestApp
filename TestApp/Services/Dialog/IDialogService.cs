using System.IO;

namespace TestApp.Services.Dialog;

public interface IDialogService
{
    public FileInfo? LoadFileDialog(string extensionsFile);

    public FileInfo? SaveFileDialog(string extensionsFile, string? fileName = null);

    public void ShowMessage(string title, string message);

    public bool ShowQuestion(string title, string message);
}