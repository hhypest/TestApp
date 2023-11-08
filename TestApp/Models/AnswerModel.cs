namespace TestApp.Models;

public record class AnswerModel
{
    public string TitleAnswer { get; init; }

    public bool IsAnswered { get; init; }

    public AnswerModel(string titleAnswer, bool isAnswered)
    {
        TitleAnswer = titleAnswer;
        IsAnswered = isAnswered;
    }
}