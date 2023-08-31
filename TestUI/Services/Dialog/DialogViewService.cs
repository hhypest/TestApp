using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.IO;
using System.Linq;
using System.Windows;
using TestCore.Services.Dialog;
using TestUI.Views.Dialogs.Errors;
using TestUI.Views.Dialogs.Informations;
using TestUI.Views.Dialogs.Questions;
using TestUI.Views.Dialogs.Warnings;

namespace TestUI.Services.Dialog;

public sealed class DialogViewService : IDialogViewService
{
    private static Window? ActiveWindow => Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive);

    private static Window? FocusedWindow => Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsFocused);

    private static Window? CurrentWindow => FocusedWindow ?? ActiveWindow;

    public FileInfo? OpenFileDialog(string titleDialog, string fileFilter = "Все файлы|*.*")
    {
        var dialog = new OpenFileDialog()
        {
            Title = titleDialog,
            Filter = fileFilter,
            RestoreDirectory = true,
            AddExtension = true,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(CurrentWindow) == false)
            return null;

        return new FileInfo(dialog.FileName);
    }

    public FileInfo? SaveFileDialog(string titleDialog, string fileFilter = "Все файлы|*.*", string? fileName = null)
    {
        var dialog = new SaveFileDialog()
        {
            Title = titleDialog,
            Filter = fileFilter,
            FileName = fileName ?? "Новый файл",
            RestoreDirectory = true,
            AddExtension = true
        };

        if (dialog.ShowDialog(CurrentWindow) == false)
            return null;

        return new FileInfo(dialog.FileName);
    }

    public void ShowErrorMessage(string titleView, string messageView)
    {
        var view = ActivatorUtilities.GetServiceOrCreateInstance<IErrorsWindow>(App.AppHost.Services);
        view.SetOwnerView(CurrentWindow);
        view.ShowView(titleView, messageView);
    }

    public void ShowInformationMessage(string titleView, string messageView)
    {
        var view = ActivatorUtilities.GetServiceOrCreateInstance<IInformationsWindow>(App.AppHost.Services);
        view.SetOwnerView(CurrentWindow);
        view.ShowView(titleView, messageView);
    }

    public void ShowWarningMessage(string titleView, string messageView)
    {
        var view = ActivatorUtilities.GetServiceOrCreateInstance<IWarningsWindow>(App.AppHost.Services);
        view.SetOwnerView(CurrentWindow);
        view.ShowView(titleView, messageView);
    }

    public bool ShowQuestionMessage(string titleView, string messageView)
    {
        var view = ActivatorUtilities.GetServiceOrCreateInstance<IQuestionsWindow>(App.AppHost.Services);
        view.SetOwnerView(CurrentWindow);
        view.ShowView(titleView, messageView);

        return view.ResultShowView;
    }
}