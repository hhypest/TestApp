using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using TestApp.Services.Dialog;
using TestApp.Services.Factory;
using TestApp.Services.Navigation;
using TestApp.ViewModels.Answer;
using TestApp.ViewModels.Ask;
using TestApp.ViewModels.Launch;
using TestApp.ViewModels.Test;
using TestApp.Views.Dialog;
using TestApp.Views.Pages.Ask;
using TestApp.Views.Pages.Launch;
using TestApp.Views.Pages.Test;
using TestApp.Views.Shell;

namespace TestApp.Extensions;

public static class AppBuilder
{
    private static void AddServices(this IServiceCollection services)
    {
        services.AddSingleton<IMessenger, StrongReferenceMessenger>();
        services.AddSingleton<IFactoryService, FactoryService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddTransient<IDialogService, DialogService>();
    }

    private static void AddViewModels(this IServiceCollection services)
    {
        services.AddSingleton<ITestViewModel, TestViewModel>();
        services.AddTransient<ILaunchViewModel, LaunchViewModel>();
        services.AddTransient<IAskViewModel, AskViewModel>();
        services.AddTransient<IAnswerViewModel, AnswerViewModel>();
    }

    private static void AddViews(this IServiceCollection services)
    {
        services.AddSingleton<IShellView, ShellView>();
        services.AddTransient<ILaunchView, LaunchView>();
        services.AddTransient<ITestView, TestView>();
        services.AddTransient<IAskView, AskView>();
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
        var navigationService = ActivatorUtilities.GetServiceOrCreateInstance<INavigationService>(host.Services);
        navigationService.StartService();
    }

    public static void AppStoped(this IHost host)
    {
        var navigationService = ActivatorUtilities.GetServiceOrCreateInstance<INavigationService>(host.Services);
        navigationService.StopService();
    }
}