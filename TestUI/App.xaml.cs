using Microsoft.Extensions.Hosting;
using System.Windows;
using TestUI.Extensions;

namespace TestUI;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;

    public App()
    {
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext,  services) =>
            {
                services.AddService();
                services.AddViewModels();
                services.AddViews();
            }).Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost.StartAsync();
        AppHost.AppStarted();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await AppHost.StopAsync();
        base.OnExit(e);
    }
}