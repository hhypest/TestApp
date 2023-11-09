using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using TestApp.Extensions;
using TestApp.Messages.TestMessages;
using TestApp.Services.Dialog;

namespace TestApp.ViewModels.Launch;

public partial class LaunchViewModel : ObservableValidator, ILaunchViewModel
{
    #region Зависимости

    private readonly IMessenger _messenger;
    private readonly ILogger _logger;
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

    public LaunchViewModel(IMessenger messenger, ILogger<LaunchViewModel> logger, IDialogService dialogService)
    {
        _messenger = messenger;
        _logger = logger;
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
        _dialogService.ShowMessage("Внимание", "Данный функционал, к сожалению, не реализован. Разработчик работает на реализацией.");
        await Task.Delay(1000);
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