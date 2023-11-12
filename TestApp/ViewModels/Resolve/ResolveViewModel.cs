using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using TestApp.Extensions;
using TestApp.Messages.ResolveMessage;
using TestApp.Models;
using TestApp.Services.Factory;
using TestApp.Services.Navigation;
using TestApp.ViewModels.Answer;
using TestApp.ViewModels.Result;

namespace TestApp.ViewModels.Resolve;

public partial class ResolveViewModel : ObservableRecipient, IRecipient<CreateResolveMessage>, IResolveViewModel
{
    #region Зависимости

    private readonly IFactoryService _factoryService;
    private readonly INavigationService _navigationService;

    #endregion Зависимости

    #region Свойства модели представления

    [ObservableProperty]
    private string _titleAsk = null!;

    [ObservableProperty]
    private int _countAsks;

    [ObservableProperty]
    private int _currentCountAsks;

    [ObservableProperty]
    private bool _isSingleAnswered;

    [ObservableProperty]
    private ObservableCollection<IAnswerViewModel> _answersList = null!;

    private Stack<AskModel> AsksStack { get; set; } = null!;

    private List<AskModel> AsksWrong { get; set; } = null!;

    private AskModel CurrentAsk { get; set; } = null!;

    private string TitleTest { get; set; } = null!;

    public bool IsSubscribeMessage
    {
        set
        {
            IsActive = value;
        }
    }

    #endregion Свойства модели представления

    #region Обработка сообщений

    public void Receive(CreateResolveMessage message)
    {
        var asksList = message.Value.AsksList;
        TitleTest = message.Value.TitleTest;
        CountAsks = asksList.Count;
        CurrentCountAsks = 0;
        ShuffleAsksList(asksList);

        AsksStack = new(asksList);
        AsksWrong = new();
        NextAsk(AsksStack.Pop());
    }

    #endregion Обработка сообщений

    #region Конструктор

    public ResolveViewModel(IMessenger messenger,
                            IFactoryService factoryService,
                            INavigationService navigationService)
        : base(messenger)
    {
        _factoryService = factoryService;
        _navigationService = navigationService;
    }

    #endregion Конструктор

    #region Функции прохождения теста

    private static void ShuffleAsksList(List<AskModel> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var index = RandomNumberGenerator.GetInt32(list.Count - 1);
            (list[index], list[i]) = (list[i], list[index]);
        }
    }

    private void NextAsk(AskModel ask)
    {
        CurrentAsk = ask;
        IsSingleAnswered = ask.IsSingleAnswer;
        AnswersList = new(ask.AnswersList.Select(answer =>
        {
            var answerViewModel = answer.GetAnswerViewModel(_factoryService);
            answerViewModel.IsAnswered = false;
            return answerViewModel;
        }));
        TitleAsk = ask.TitleAsk;
    }

    private bool CheckAsk()
    {
        if (!AnswersList.Any(answer => answer.IsAnswered) || (IsSingleAnswered && AnswersList.Count(answer => answer.IsAnswered) > 1))
            return false;

        var result = 0;
        var list = new List<AnswerModel>(AnswersList.Select(answer => answer.GetAnswerModel()));

        for (int i = 0; i < CurrentAsk.AnswersList.Count; i++)
        {
            if (CurrentAsk.AnswersList[i].Equals(list[i]))
                continue;

            result++;
        }

        return result < 1;
    }

    private string GetResult()
    {
        var asksWrong = 0.0;
        switch (AsksWrong.Count, CountAsks)
        {
            case ( > 0, > 0) when AsksWrong.Count == CountAsks:
                break;

            case (0, > 0):
                asksWrong = 100.0;
                break;

            default:
                asksWrong = Math.Abs(((Convert.ToDouble(AsksWrong.Count) / Convert.ToDouble(CountAsks)) - 1.00) * 100.0);
                break;
        }

        var result = CountAsks - AsksWrong.Count;
        return $"Тест <{TitleTest}> закончен. Количество правильных ответов - {result} из {CountAsks}. Результат {asksWrong}%";
    }

    #endregion Функции прохождения теста

    #region Команды

    [RelayCommand]
    private void RelayNextAsk()
    {
        CurrentCountAsks++;

        if (CurrentCountAsks == CountAsks)
        {
            if (!CheckAsk())
                AsksWrong.Add(CurrentAsk);

            var result = _factoryService.CreateViewModel<IResultViewModel>(NavigationType.Result);
            result.AsksList = new(AsksWrong.Select(ask => ask.GetAskViewModel(_factoryService)));
            result.ResultTest = GetResult();
            result.IsVisibleExpander = AsksWrong.Count switch
            {
                > 0 => true,
                _ => false
            };
            IsSubscribeMessage = false;
            _navigationService.NavigationTo(NavigationType.Result, result);
            return;
        }

        if (!CheckAsk())
            AsksWrong.Add(CurrentAsk);

        NextAsk(AsksStack.Pop());
    }

    #endregion Команды
}