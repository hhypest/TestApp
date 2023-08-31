namespace TestData.Models.Answer;

public record class AnswerModel
{
    public string OptionAnswer { get; init; }

    public bool IsAnswered { get; init; }

    public AnswerModel(string optionAnswer, bool isAnswered)
    {
        OptionAnswer = optionAnswer;
        IsAnswered = isAnswered;
    }
}