using CommunityToolkit.Mvvm.Input;

namespace TestApp.ViewModels.Launch;

public interface ILaunchViewModel
{
    public string TitleTest { get; set; }

    public bool IsOpenDialog { get; set; }

    public IRelayCommand CreateNewTestCommand { get; }

    public IAsyncRelayCommand LoadTestCommand { get; }

    public IAsyncRelayCommand ResolveTestCommand { get; }

    public IRelayCommand<string?> AcceptCreateTestCommand { get; }

    public IRelayCommand CancelCreateTestCommand { get; }
}