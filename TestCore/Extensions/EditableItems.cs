namespace TestCore.Extensions;

public readonly record struct EditAsk(string QuestionAsk, bool IsSingleAnswer, List<EditAnswer> AnswersList);

public readonly record struct EditAnswer(string OptionAnswer, bool IsAnswered);