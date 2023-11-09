using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using TestApp.Extensions;
using TestApp.Views.Dialog;

namespace TestApp.Services.Dialog;

public sealed class DialogService : IDialogService
{
    #region Зависимости

    private readonly IServiceProvider _services;

    #endregion Зависимости

    #region Определение активного окна

    private static Window? ActiveWindow => Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive);

    private static Window? FocusedWindow => Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsFocused);

    private static Window? CurrentWindow => FocusedWindow ?? ActiveWindow;

    #endregion Определение активного окна

    #region Конструктор
    public DialogService(IServiceProvider services)
    {
        _services = services;
    }
    #endregion

    #region Реализация интерфейса
    public FileInfo? LoadFileDialog(string extensionsFile)
    {
        var dialog = new OpenFileDialog()
        {
            CheckPathExists = true,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = false,
            Filter = extensionsFile,
            AddExtension = false,
            Title = "Загрузка файла теста"
        };

        if (dialog.ShowDialog() == false)
            return null;

        return new FileInfo(dialog.FileName);
    }

    public FileInfo? SaveFileDialog(string extensionsFile, string? fileName = null)
    {
        var dialog = new SaveFileDialog()
        {
            RestoreDirectory = false,
            Filter = extensionsFile,
            FileName = fileName ?? "Новый тест",
            AddExtension = false,
            Title = "Сохранение файла теста"
        };

        if (dialog.ShowDialog() == false)
            return null;

        return new FileInfo(dialog.FileName);
    }

    public void ShowMessage(string title, string message)
    {
        var dialog = ActivatorUtilities.GetServiceOrCreateInstance<IDialogView>(_services);
        dialog.CreateView(title, message, TypeDialogView.ErrorDialog);
        dialog.SetOwner(CurrentWindow!);
        dialog.ShowView();
    }

    public bool ShowQuestion(string title, string message)
    {
        var dialog = ActivatorUtilities.GetServiceOrCreateInstance<IDialogView>(_services);
        dialog.CreateView(title, message, TypeDialogView.QuestionDialog);
        dialog.SetOwner(CurrentWindow!);
        dialog.ShowView();

        return dialog.ResultDialog;
    }
    #endregion
}