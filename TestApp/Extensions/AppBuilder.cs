using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using TestApp.Services.Dialog;
using TestApp.Views.Dialog;
using TestApp.Views.Shell;

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
        services.AddTransient<IDialogView, DialogView>();
    }

    public static IHost AppBuild(this IHostBuilder hostBuilder)
    {
        return hostBuilder.ConfigureAppConfiguration(config => config.AddJsonFile($@"{AppContext.BaseDirectory}settings\appsettings.json").Build())
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
        shell.ShowView();
    }

    public static void AppStoped(this IHost host)
    {
    }
}