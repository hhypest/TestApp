using MaterialDesignThemes.Wpf;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TestUI.Views.Launcher;

public partial class MainView : Window, IMainView
{
    #region Закрытые свойства
    private bool IsMaximizeWindow {  get; set; }
    #endregion

    #region Конструктор
    public MainView()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
    }
    #endregion

    #region Реализация интерфейса
    public void ShowView()
    {
        Show();
    }

    public void SetDataContext<T>(T dataContext)
    {
        DataContext = dataContext;
    }

    public void NavigationTo<T>(T view)
    {
        PresenterPages.NavigationService.Navigate(view);
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

        if (WindowState is WindowState.Normal || WindowState is WindowState.Minimized)
        {
            IsMaximizeWindow = false;
            ReMaximizeIcon.Kind = PackIconKind.WindowMaximize;
        }
        else
        {
            IsMaximizeWindow = true;
            ReMaximizeIcon.Kind = PackIconKind.WindowRestore;
        }
    }

    private void OnCloseAppClicked(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void OnMaximizeClicked(object sender, RoutedEventArgs e)
    {
        switch (IsMaximizeWindow)
        {
            case false:
                WindowState = WindowState.Maximized;
                IsMaximizeWindow = true;
                ReMaximizeIcon.Kind = PackIconKind.WindowRestore;
                break;
            case true:
                WindowState = WindowState.Normal;
                IsMaximizeWindow = false;
                ReMaximizeIcon.Kind = PackIconKind.WindowMaximize;
                break;
        }
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    #endregion
}