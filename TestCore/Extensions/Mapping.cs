using TestCore.Services.Navigation;
using TestCore.ViewModels.Answer;
using TestCore.ViewModels.Ask;
using TestCore.ViewModels.Test;
using TestData.Models.Answer;
using TestData.Models.Ask;
using TestData.Models.Test;

namespace TestCore.Extensions;

public static class Mapping
{
    public static TestModel ToModel(this ITestViewModel test)
    {
        return new(test.TitleTest, test.AsksList.Select(ask => ask.ToModel()).ToList());
    }

    private static AskModel ToModel(this IAskViewModel ask)
    {
        return new(ask.QuestionAsk, ask.IsSingleAnswer, ask.AnswersList.Select(answer => answer.ToModel()).ToList());
    }

    private static AnswerModel ToModel(this IAnswerViewModel answer)
    {
        return new(answer.OptionAnswer, answer.IsAnswered);
    }

    public static void ToViewModel(this TestModel test, ITestViewModel viewModel, INavigationService navigationService)
    {
        viewModel.TitleTest = test.TitleTest;
        viewModel.AsksList = new(test.AsksList.Select(ask => ask.ToViewModel(navigationService)));
    }

    private static IAskViewModel ToViewModel(this AskModel ask, INavigationService navigationService)
    {
        var result = navigationService.GetAskInstance();
        result.QuestionAsk = ask.QuestionAsk;
        result.IsSingleAnswer = ask.IsSingleAnswer;
        result.AnswersList = new(ask.AnswersList.Select(answer => answer.ToViewModel(navigationService)));

        return result;
    }

    private static IAnswerViewModel ToViewModel(this AnswerModel answer, INavigationService navigationService)
    {
        var result = navigationService.GetAnswerInstance();
        result.OptionAnswer = answer.OptionAnswer;
        result.IsAnswered = answer.IsAnswered;

        return result;
    }
}