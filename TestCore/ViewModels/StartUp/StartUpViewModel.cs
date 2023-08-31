using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.ComponentModel.DataAnnotations;
using TestCore.Extensions;
using TestCore.Messages;
using TestCore.Services.Dialog;
using TestCore.Services.Navigation;
using TestCore.ViewModels.Test;
using TestData.Extensions;

namespace TestCore.ViewModels.StartUp;

public partial class StartUpViewModel : ObservableValidator, IStartUpViewModel
{
    #region Зависимости
    private readonly IDialogViewService _dialogService;
    private readonly INavigationService _navigationService;
    #endregion

    #region Свойства модели представления
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Поле обязательно для заполнения!")]
    [MinLength(5, ErrorMessage = "Название теста должно состоять минимум из пяти символов!")]
    private string _titleTest;

    [ObservableProperty]
    private bool _isOpenStartUpDialog;
    #endregion

    #region Конструктор
    public StartUpViewModel(IDialogViewService dialogService, INavigationService navigationService)
    {
        _titleTest = "Новый тест";
        _dialogService = dialogService;
        _navigationService = navigationService;
    }
    #endregion

    #region Команды
    [RelayCommand]
    private void OnCreateNewTest()
    {
        IsOpenStartUpDialog = true;
    }

    [RelayCommand]
    private async Task OnLoadTest()
    {
        var file = _dialogService.OpenFileDialog("Какой тест открыть?", "Файл теста|*.test");
        if (file is null)
            return;

        try
        {
            var test = await SerializeExtension.LoadJson(file.FullName);
            var viewModel = _navigationService.GetTestInstance();
            viewModel.CountAsk = test.AsksList.Count;
            viewModel.IsSaveTest = true;
            viewModel.PathSave = file.FullName;

            test.ToViewModel(viewModel, _navigationService);
            _navigationService.NavigationTo(typeof(ITestViewModel));
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorMessage("Внимание!", $"Произошла ошибка!\n{ex.Message}\n{ex.Source}");
        }
    }

    [RelayCommand(CanExecute = nameof(OnCanExecuteTitle))]
    private void OnOkDialogStartUp(string? title)
    {
        _navigationService.NavigationTo(typeof(ITestViewModel));
        WeakReferenceMessenger.Default.Send(new TitleTestMessage(title!));
    }

    [RelayCommand]
    private void OnCancelDialogStartUp()
    {
        IsOpenStartUpDialog = false;
        TitleTest = "Новый тест";
    }
    #endregion

    #region Предикаты
    private bool OnCanExecuteTitle(string? title)
    {
        return !string.IsNullOrWhiteSpace(title) && title.Length >= 5;
    }
    #endregion
}