using MaterialDesignThemes.Wpf;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TestApp.Views.Dialog.DialogContent;

namespace TestApp.Views.Dialog;

public partial class DialogView : Window, IDialogView
{
    #region Константы
    private const PackIconKind _error = PackIconKind.ErrorOutline;
    private const PackIconKind _question = PackIconKind.QuestionBoxOutline;
    private const PackIconKind _warning = PackIconKind.WarningOutline;

    private const int _wMsg = 161;
    private const int _wParam = 2;
    private const int _lParam = 0;
    #endregion

    #region Конструктор
    public DialogView()
    {
        InitializeComponent();
    }
    #endregion

    #region Реализация интерфейса
    public void CreateView(string title, string message, TypeDialogView typeDialog)
    {
        CaptionView.Text = title;
        MessageView.Text = message;

        switch (typeDialog)
        {
            case TypeDialogView.InformationDialog:
                IconView.Kind = _warning;
                ContentView.Content = new InformationDialog(this);
                break;
            case TypeDialogView.QuestionDialog:
                IconView.Kind = _question;
                ContentView.Content = new QuestionDialog(this);
                break;
            case TypeDialogView.ErrorDialog:
                IconView.Kind = _error;
                ContentView.Content = new InformationDialog(this);
                break;
        }
    }

    public void ShowView()
    {
        ShowDialog();
    }
    #endregion

    #region Системные вызовы
    [LibraryImport("user32.dll", EntryPoint = "SendMessageA")]
    private static partial IntPtr SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);
    #endregion

    private void OnMoveChanged(object sender, MouseButtonEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        SendMessage(helper.Handle, _wMsg, _wParam, _lParam);
    }
}