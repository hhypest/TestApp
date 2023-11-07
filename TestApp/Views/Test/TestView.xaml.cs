﻿using System.Windows.Controls;

namespace TestApp.Views.Test;

public partial class TestView : Page, ITestView
{
    #region Конструктор
    public TestView()
    {
        InitializeComponent();
    }
    #endregion

    #region Реализация интерфейса
    public void NavigationTo<T>(T page)
    {
        NavigationService.Navigate(page);
    }

    public void SetDataContext<T>(T dataContext)
    {
        DataContext = dataContext;
    }
    #endregion
}