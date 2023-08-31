using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TestCore.Services.Dialog;
using TestCore.Services.Navigation;
using TestCore.ViewModels.Answer;
using TestCore.ViewModels.Ask;
using TestCore.ViewModels.StartUp;
using TestCore.ViewModels.Test;
using TestUI.Services.Dialog;
using TestUI.Services.Navigation;
using TestUI.Views.Dialogs.Errors;
using TestUI.Views.Dialogs.Informations;
using TestUI.Views.Dialogs.Questions;
using TestUI.Views.Dialogs.Warnings;
using TestUI.Views.Launcher;
using TestUI.Views.Pages.Ask;
using TestUI.Views.Pages.StartUp;
using TestUI.Views.Pages.Test;

namespace TestUI.Extensions;

public static class AppBuilder
{
    public static void AddService(this IServiceCollection services)
    {
        services.AddSingleton<IDialogViewService, DialogViewService>();
        services.AddSingleton<INavigationService, NavigationService>();
    }

    public static void AddViewModels(this IServiceCollection services)
    {
        services.AddSingleton<ITestViewModel, TestViewModel>();
        services.AddTransient<IStartUpViewModel, StartUpViewModel>();
        services.AddTransient<IAskViewModel, AskViewModel>();
        services.AddTransient<IAnswerViewModel, AnswerViewModel>();
    }

    public static void AddViews(this IServiceCollection services)
    {
        services.AddSingleton<IMainView, MainView>();
        services.AddTransient<IErrorsWindow, ErrorsWindow>();
        services.AddTransient<IInformationsWindow, InformationsWindow>();
        services.AddTransient<IQuestionsWindow, QuestionsWindow>();
        services.AddTransient<IWarningsWindow, WarningsWindow>();
        services.AddTransient<IStartUpView, StartUpView>();
        services.AddTransient<ITestView, TestView>();
        services.AddTransient<IAskView, AskView>();
    }

    public static void AppStarted(this IHost host)
    {
        var launchView = ActivatorUtilities.GetServiceOrCreateInstance<IMainView>(host.Services);
        launchView.ShowView();
        var navigation = ActivatorUtilities.GetServiceOrCreateInstance<INavigationService>(host.Services);
        navigation.NavigationTo(typeof(IStartUpViewModel));
    }
}