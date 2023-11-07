using Microsoft.Extensions.Hosting;
using System.Windows;
using TestApp.Extensions;

namespace TestApp;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;

    public App()
    {
        AppHost = Host.CreateDefaultBuilder().AppBuild();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost.StartAsync();
        AppHost.AppStarted();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        AppHost.AppStoped();
        await AppHost.StopAsync();
        base.OnExit(e);
    }
}