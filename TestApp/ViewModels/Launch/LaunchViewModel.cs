﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using TestApp.Extensions;
using TestApp.Messages.ResolveMessage;
using TestApp.Messages.TestMessages;
using TestApp.Services.Dialog;
using TestApp.Services.Factory;
using TestApp.Services.Navigation;
using TestApp.ViewModels.Resolve;

namespace TestApp.ViewModels.Launch;

public partial class LaunchViewModel : ObservableValidator, ILaunchViewModel
{
    #region Зависимости

    private readonly IMessenger _messenger;
    private readonly ILogger _logger;
    private readonly IFactoryService _factoryService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;

    #endregion Зависимости

    #region Свойства модели представления

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Название теста не может быть пустым!")]
    [MinLength(5, ErrorMessage = "Название должно состоять минимум из пяти символов!")]
    private string _titleTest;

    [ObservableProperty]
    private bool _isOpenDialog;

    #endregion Свойства модели представления

    #region Конструктор

    public LaunchViewModel(IMessenger messenger,
                           ILogger<LaunchViewModel> logger,
                           IFactoryService factoryService,
                           INavigationService navigationService,
                           IDialogService dialogService)
    {
        _messenger = messenger;
        _logger = logger;
        _factoryService = factoryService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _titleTest = "Новый тест";
    }

    #endregion Конструктор

    #region Команды

    [RelayCommand]
    private void CreateNewTest()
    {
        IsOpenDialog = true;
    }

    [RelayCommand]
    private async Task LoadTest()
    {
        var filePath = _dialogService.LoadFileDialog("Файл теста|*.json");
        if (filePath is null)
            return;

        try
        {
            var test = await DataUtils.LoadTestFile(filePath.FullName);
            _messenger.Send(new LoadTestMessage((test, filePath.FullName)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при открытии файла");
            _dialogService.ShowMessage("Ошибка", $"Ошибка при открытии файла\n{ex.Message}\n{ex.StackTrace}\n{ex.Source}");
        }
    }

    [RelayCommand]
    private async Task ResolveTest()
    {
        var filePath = _dialogService.LoadFileDialog("Файл теста|*.json");
        if (filePath is null)
            return;

        try
        {
            var test = await DataUtils.LoadTestFile(filePath.FullName);
            var resolve = _factoryService.CreateViewModel<IResolveViewModel>(NavigationType.Resolve);
            resolve.IsSubscribeMessage = true;
            _messenger.Send(new CreateResolveMessage(test));
            _navigationService.NavigationTo(NavigationType.Resolve, resolve);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при открытии файла");
            _dialogService.ShowMessage("Ошибка", $"Ошибка при открытии файла\n{ex.Message}\n{ex.StackTrace}\n{ex.Source}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAcceptCreateTest))]
    private void AcceptCreateTest(string? title)
    {
        _messenger.Send(new CreateTestMessage(title!));
    }

    [RelayCommand]
    private void CancelCreateTest()
    {
        IsOpenDialog = false;
        TitleTest = "Новый тест";
    }

    #endregion Команды

    #region Предикаты

    private bool CanExecuteAcceptCreateTest(string? title)
    {
        return !string.IsNullOrWhiteSpace(title) && title.Length > 4;
    }

    #endregion Предикаты
}