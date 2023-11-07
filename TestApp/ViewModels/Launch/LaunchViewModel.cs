using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace TestApp.ViewModels.Launch;

public partial class LaunchViewModel : ObservableValidator, ILaunchViewModel
{
    #region Свойства модели представления
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Название теста не может быть пустым!")]
    [MinLength(5, ErrorMessage = "Название должно состоять минимум из пяти символов!")]
    private string _titleTest;

    [ObservableProperty]
    private bool _isOpenDialog;
    #endregion

    #region Конструктор
    public LaunchViewModel()
    {
        _titleTest = "Новый тест";
    }
    #endregion

    #region Команды
    [RelayCommand]
    private void CreateNewTest()
    {
        IsOpenDialog = true;
    }

    [RelayCommand]
    private Task LoadTest()
    {
        throw new NotSupportedException();
    }

    [RelayCommand]
    private Task ResolveTest()
    {
        throw new NotSupportedException();
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAcceptCreateTest))]
    private void AcceptCreateTest(string? title)
    {
        throw new NotSupportedException();
    }

    [RelayCommand]
    private void CancelCreateTest()
    {
        IsOpenDialog = false;
        TitleTest = "Новый тест";
    }
    #endregion

    #region Предикаты
    private bool CanExecuteAcceptCreateTest(string? title)
    {
        return !string.IsNullOrWhiteSpace(title) && title.Length > 4;
    }
    #endregion
}