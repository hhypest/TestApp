using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using TestApp.Extensions;
using TestApp.View;

namespace TestApp;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;

    public App()
        => AppHost = HostingEx.GetHostBuilder().Build();

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost.StartAsync();
        var shell = AppHost.Services.GetRequiredService<Shell>();
        shell.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await AppHost.StopAsync();
        base.OnExit(e);
    }
}