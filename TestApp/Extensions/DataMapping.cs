using DataModel.DataRecord;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TestApp.Service.ErrorsMessage;
using TestApp.ViewModel.AppViewModel;
using TestApp.ViewModel.AppViewModel.Interfaces;

namespace TestApp.Extensions;

internal static class DataMapping
{
    internal static TestModel ViewModelMap(this ITestViewModel test)
        => new(test.TitleTest, test.Asks.GetAsks());

    private static IList<AskModel> GetAsks(this ICollection<IAskViewModel> asks)
        => asks.Select(ask => new AskModel(ask.Question, ask.IsSingleAnswers, ask.Answers.GetAnswers())).ToList();

    private static IList<AnswerModel> GetAnswers(this ICollection<IAnswerViewModel> answers)
        => answers.Select(answer => new AnswerModel(answer.Option, answer.IsAnswered)).ToList();

    internal static ITestViewModel DataModelMap(this TestModel test, IServiceProvider service)
        => new TestViewModel(service)
        {
            TitleTest = test.TitleTest,
            Asks = new ObservableCollection<IAskViewModel>(test.Asks.GetAsks(service)),
            CountAsk = test.Asks.Count
        };

    private static IEnumerable<IAskViewModel> GetAsks(this IList<AskModel> asks, IServiceProvider service)
        => asks.Select(ask => new AskViewModel(service)
        {
            Question = ask.Question,
            IsSingleAnswers = ask.IsSingleAnswers,
            Answers = new ObservableCollection<IAnswerViewModel>(ask.Answers.GetAnswers(service))
        });

    private static IEnumerable<IAnswerViewModel> GetAnswers(this IList<AnswerModel> answers, IServiceProvider service)
        => answers.Select(answer => new AnswerViewModel(service.GetRequiredService<IErrorsMessage>())
        {
            Option = answer.Option,
            IsAnswered = answer.IsAnswered
        });
}