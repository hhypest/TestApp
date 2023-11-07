using System.Runtime.InteropServices;
using System;
using System.Windows;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using System.Windows.Interop;
using System.Diagnostics;

namespace TestApp.Views.Shell;

public partial class ShellView : Window, IShellView
{
    #region Константы
    private const PackIconKind _maxIcon = PackIconKind.WindowMaximize;
    private const PackIconKind _restoreIcon = PackIconKind.WindowRestore;

    private const int _wMsg = 161;
    private const int _wParam = 2;
    private const int _lParam = 0;
    #endregion

    #region Свойства окна
    private bool IsStateWindow {  get; set; }
    #endregion

    #region Конструктор
    public ShellView()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;

#if DEBUG
        Debug.WriteLine(this, "Конструктор главного окна");
#endif
    }
    #endregion

    #region Реализация интерфейса
    public void NavigationTo<T>(T page)
    {
        PresenterViews.NavigationService.Navigate(page);

#if DEBUG
        Debug.WriteLine(page, "Навигация до представления");
#endif
    }

    public void SetDataContext<T>(T dataContext)
    {
        DataContext = dataContext;

#if DEBUG
        Debug.WriteLine(dataContext, "Установка контекста");
#endif
    }

    public void ShowView()
    {
        Show();

#if DEBUG
        Debug.WriteLine(this, "Вывод окна");
#endif
    }
    #endregion

    #region Системные вызовы
    [LibraryImport("user32.dll", EntryPoint = "SendMessageA")]
    private static partial IntPtr SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);
    #endregion

    #region Реализация поведения окна
    private void OnMoveChanged(object sender, MouseButtonEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        SendMessage(helper.Handle, _wMsg, _wParam, _lParam);

        if (WindowState is WindowState.Normal ||  WindowState is WindowState.Minimized)
        {
            IsStateWindow = false;
            IconMax.Kind = _maxIcon;
        }
        else
        {
            IsStateWindow = true;
            IconMax.Kind = _restoreIcon;
        }
    }

    private void OnExitAppClicked(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void OnMaxAppClicked(object sender, RoutedEventArgs e)
    {
        switch (IsStateWindow)
        {
            case true:
                WindowState = WindowState.Normal;
                IsStateWindow = false;
                IconMax.Kind = _maxIcon;
                break;
            case false:
                WindowState = WindowState.Maximized;
                IsStateWindow = true;
                IconMax.Kind = _restoreIcon;
                break;
        }
    }

    private void OnMinAppClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    #endregion
}