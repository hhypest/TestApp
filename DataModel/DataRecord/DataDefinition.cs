namespace DataModel.DataRecord;

public readonly record struct AnswerModel(string Option, bool IsAnswered);

public readonly record struct AskModel(string Question, bool IsSingleAnswers, IList<AnswerModel> Answers);

public readonly record struct TestModel(string TitleTest, IList<AskModel> Asks);