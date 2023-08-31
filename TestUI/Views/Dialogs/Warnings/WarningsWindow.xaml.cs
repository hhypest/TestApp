using System.Runtime.InteropServices;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TestUI.Views.Dialogs.Warnings;

public partial class WarningsWindow : Window, IWarningsWindow
{
    #region Публичное свойство
    public bool ResultShowView => DialogResult == true;
    #endregion

    #region Коструктор
    public WarningsWindow()
    {
        InitializeComponent();
    }
    #endregion

    #region Реализация интерфейса
    public void ShowView(string title, string message)
    {
        TitleTb.Text = title;
        DetailsTb.Text = message;
        ShowDialog();
    }

    public void SetOwnerView(Window? owner)
    {
        Owner = owner;
    }
    #endregion

    #region Системные вызовы
    [LibraryImport("user32.dll", EntryPoint = "SendMessageA")]
    private static partial IntPtr SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);
    #endregion

    #region Реализация поведения окна
    private void OnDragMoveClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton is not MouseButtonState.Pressed)
            return;

        var helper = new WindowInteropHelper(this);
        SendMessage(helper.Handle, 161, 2, 0);
    }

    private void OnCloseViewClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
    #endregion
}