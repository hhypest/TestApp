﻿using System.Windows;
using System;
using System.Linq;
using TestApp.Views.Dialog;
using Microsoft.Extensions.DependencyInjection;

namespace TestApp.Services.Dialog;

public sealed class DialogService : IDialogService
{
    #region Зависимости
    private readonly IServiceProvider _services;
    #endregion

    #region Определение активного окна
    private static Window? ActiveWindow => Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive);

    private static Window? FocusedWindow => Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsFocused);

    private static Window? CurrentWindow => FocusedWindow ?? ActiveWindow;
    #endregion

    public DialogService(IServiceProvider services)
    {
        _services = services;
    }

    public void ShowMessage(string title, string message, TypeDialogView typeDialog)
    {
        var dialog = ActivatorUtilities.GetServiceOrCreateInstance<IDialogView>(_services);
        dialog.CreateView(title, message, typeDialog);
        dialog.SetOwner(CurrentWindow!);
        dialog.ShowView();
    }

    public bool ShowQuestion(string title, string message, TypeDialogView typeDialog)
    {
        var dialog = ActivatorUtilities.GetServiceOrCreateInstance<IDialogView>(_services);
        dialog.CreateView(title, message, typeDialog);
        dialog.SetOwner(CurrentWindow!);
        dialog.ShowView();

        return dialog.ResultDialog;
    }
}