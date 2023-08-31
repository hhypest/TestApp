using TestData.Models.Answer;

namespace TestData.Models.Ask;

public record class AskModel
{
    public string QuestionAsk { get; init; }

    public bool IsSingleAnswer { get; init; }

    public List<AnswerModel> AnswersList { get; init; }

    public AskModel(string questionAsk, bool isSingleAnswer, List<AnswerModel> answersList)
    {
        QuestionAsk = questionAsk;
        IsSingleAnswer = isSingleAnswer;
        AnswersList = answersList;
    }
}