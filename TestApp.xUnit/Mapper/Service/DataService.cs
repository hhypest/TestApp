using TestApp.Core.Types;
using TestApp.Domain.Entities;

namespace TestApp.xUnit.Mapper.Service;

internal class DataService : IDataService
{
    TestData IDataService.GetDataObject()
    {
        return CreateData(10, 5);
    }

    TestEntity IDataService.GetEntity()
    {
        throw new NotImplementedException();
    }
    
    private static TestData CreateData(int countAsk, int countAnswer)
    {
        IList<AskData> asks = [];
        Guid testId = Guid.CreateVersion7();

        for (int i = 0; i < countAsk; i++)
            asks.Add(CreateData(testId, countAnswer));

        return new()
        {
            TestId = testId,
            TestTitle = "Новый тест",
            TestTime = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(45)),
            AsksList = asks
        };
    }
    
    private static AskData CreateData(Guid testId, int countAnswer)
    {
        IList<AnswerData> answers = [];
        Guid askId = Guid.CreateVersion7();

        for (int i = 0; i < countAnswer; i++)
            answers.Add(CreateData(askId));

        return new()
        {
            AskId = askId,
            TestId = testId,
            AskTitle = " Новый вопрос",
            IsSingle = true,
            AnswersList = answers
        };
    }

    private static AnswerData CreateData(Guid askId)
    {
        return new()
        {
            AnswerId = Guid.CreateVersion7(),
            AskId = askId,
            AnswerTitle = "Ответ на вопрос",
            IsCorrect = false
        };
    }
}