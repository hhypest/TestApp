using CommunityToolkit.Mvvm.Input;

namespace TestCore.ViewModels.StartUp;

public interface IStartUpViewModel
{
    public string TitleTest { get; set; }

    public bool IsOpenStartUpDialog { get; set; }

    public IRelayCommand CreateNewTestCommand { get; }

    public IAsyncRelayCommand LoadTestCommand { get; }

    public IRelayCommand<string?> OkDialogStartUpCommand { get; }

    public IRelayCommand CancelDialogStartUpCommand { get; }
}