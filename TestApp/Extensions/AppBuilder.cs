using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using TestApp.Services.Dialog;
using TestApp.Views.Dialog;
using TestApp.Views.Pages.CreateTest;
using TestApp.Views.Pages.Launch;
using TestApp.Views.Shell;
using TestApp.Views.Test;

namespace TestApp.Extensions;

public static class AppBuilder
{
    private static void AddServices(this IServiceCollection services)
    {
        services.AddTransient<IDialogService, DialogService>();
    }

    private static void AddViewModels(this IServiceCollection services)
    {
    }

    private static void AddViews(this IServiceCollection services)
    {
        services.AddSingleton<IShellView, ShellView>();
        services.AddTransient<ILaunchView, LaunchView>();
        services.AddTransient<ICreateTestView, CreateTestView>();
        services.AddTransient<ITestView, TestView>();
        services.AddTransient<IDialogView, DialogView>();
    }

    public static IHost AppBuild(this IHostBuilder hostBuilder)
    {
        return hostBuilder.ConfigureAppConfiguration(config => config.SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile(@"settings\appsettings.json").Build())
            .ConfigureServices((_, services) =>
            {
                services.AddServices();
                services.AddViewModels();
                services.AddViews();
            })
            .UseSerilog((hostContext, logConfiguration) => logConfiguration.ReadFrom.Configuration(hostContext.Configuration)).Build();
    }

    public static void AppStarted(this IHost host)
    {
        var shell = ActivatorUtilities.GetServiceOrCreateInstance<IShellView>(host.Services);
        var page = ActivatorUtilities.GetServiceOrCreateInstance<ITestView>(host.Services);
        shell.NavigationTo(page);
        shell.ShowView();
    }

    public static void AppStoped(this IHost host)
    {
    }
}