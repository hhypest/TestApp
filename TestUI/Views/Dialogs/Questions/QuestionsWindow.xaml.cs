using System.Runtime.InteropServices;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TestUI.Views.Dialogs.Questions;

public partial class QuestionsWindow : Window, IQuestionsWindow
{
    #region Публичное свойство
    public bool ResultShowView => DialogResult == true;
    #endregion

    #region Конструктор
    public QuestionsWindow()
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

    private void OnAcceptClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
    #endregion
}