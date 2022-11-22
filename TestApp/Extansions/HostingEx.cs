using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NightModel.Services.DialogMessageService;
using NightModel.Services.NavigationViewModel.NavigationContainer;
using TestApp.Service.ErrorsMessage;
using TestApp.View;
using TestApp.ViewModel.AppViewModel;
using TestApp.ViewModel.AppViewModel.Interfaces;

namespace TestApp.Extansions;

internal static class HostingEx
{
    internal static void AddTestService(this IServiceCollection services)
    {
        services.AddSingleton<IDialogMessage, DialogMessage>();
        services.AddSingleton<IErrorsMessage, ErrorsMessage>();
        services.AddSingleton<INavigationContainer, NavigationContainer>();
    }

    internal static void AddTestViewModel(this IServiceCollection services)
    {
        services.AddTransient<IStartUpViewModel, StartUpViewModel>();
        services.AddTransient<ICreateTestViewModel, CreateTestViewModel>();
        services.AddTransient<IAnswerViewModel, AnswerViewModel>();
        services.AddTransient<IAskViewModel, AskViewModel>();
        services.AddSingleton<ITestViewModel, TestViewModel>();
        services.AddSingleton<IMainViewModel, MainViewModel>();
    }

    internal static void AddTestView(this IServiceCollection services)
    {
        services.AddSingleton<Shell>();
    }

    internal static IHostBuilder GetHostBuilder()
        => Host.CreateDefaultBuilder().ConfigureServices((hostContext, services) =>
        {
            services.AddTestService();
            services.AddTestViewModel();
            services.AddTestView();
        });
}