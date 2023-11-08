using System.Collections.Generic;

namespace TestApp.Models;

public record class AskModel
{
    public string TitleAsk { get; init; }

    public bool IsSingleAnswer { get; init; }

    public List<AnswerModel> AnswersList { get; init; }

    public AskModel(string titleAsk, bool isSingleAnswer, List<AnswerModel> answersList)
    {
        TitleAsk = titleAsk;
        IsSingleAnswer = isSingleAnswer;
        AnswersList = answersList;
    }
}