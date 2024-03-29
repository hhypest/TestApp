﻿using System.Linq;
using TestApp.Models;
using TestApp.Services.Factory;
using TestApp.ViewModels.Answer;
using TestApp.ViewModels.Ask;
using TestApp.ViewModels.Test;

namespace TestApp.Extensions;

public static class MapUtils
{
    #region Модель данных в модель представления

    public static void GetTestViewModel(this ITestViewModel testViewModel, TestModel test, IFactoryService factory, string pathSave)
    {
        testViewModel.TitleTest = test.TitleTest;
        testViewModel.PathSaveTest = pathSave;
        testViewModel.CountAsk = test.AsksList.Count;
        testViewModel.IsSaveTest = true;
        testViewModel.SelectedAsk = null;
        testViewModel.AsksList = new(test.AsksList.Select(ask => ask.GetAskViewModel(factory)));
    }

    public static IAskViewModel GetAskViewModel(this AskModel ask, IFactoryService factory)
    {
        var askViewModel = factory.CreateViewModel<IAskViewModel>(NavigationType.Ask);
        askViewModel.TitleAsk = ask.TitleAsk;
        askViewModel.IsSingleAnswer = ask.IsSingleAnswer;
        askViewModel.AnswersList = new(ask.AnswersList.Select(a => a.GetAnswerViewModel(factory)));
        askViewModel.CountAnswer = ask.AnswersList.Count;

        return askViewModel;
    }

    public static IAnswerViewModel GetAnswerViewModel(this AnswerModel answer, IFactoryService factory)
    {
        var answerViewModel = factory.CreateViewModel<IAnswerViewModel>(NavigationType.Answer);
        answerViewModel.TitleAnswer = answer.TitleAnswer;
        answerViewModel.IsAnswered = answer.IsAnswered;

        return answerViewModel;
    }

    #endregion Модель данных в модель представления

    #region Модель представления в модель данных

    public static TestModel GetTestModel(this ITestViewModel test)
    {
        return new(test.TitleTest, test.AsksList.Select(ask => ask.GetAskModel()).ToList());
    }

    public static AskModel GetAskModel(this IAskViewModel ask)
    {
        return new(ask.TitleAsk, ask.IsSingleAnswer, ask.AnswersList.Select(answer => answer.GetAnswerModel()).ToList());
    }

    public static AnswerModel GetAnswerModel(this IAnswerViewModel answer)
    {
        return new(answer.TitleAnswer, answer.IsAnswered);
    }

    #endregion Модель представления в модель данных
}