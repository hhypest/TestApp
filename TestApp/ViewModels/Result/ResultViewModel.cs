using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TestApp.Services.Navigation;
using TestApp.ViewModels.Ask;

namespace TestApp.ViewModels.Result;

public partial class ResultViewModel : ObservableObject, IResultViewModel
{
    #region Зависимости
    private readonly INavigationService _navigationService;
    #endregion

    #region Свойства модели представления
    [ObservableProperty]
    private string _resultTest = null!;

    [ObservableProperty]
    private ObservableCollection<IAskViewModel> _asksList = null!;

    [ObservableProperty]
    private IAskViewModel? _selectedAsk;
    #endregion

    #region Конструктор
    public ResultViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }
    #endregion

    #region Команды
    [RelayCommand]
    private void CompleteResult()
    {
        _navigationService.NavigationTo(Extensions.NavigationType.Launch);
    }
    #endregion
}