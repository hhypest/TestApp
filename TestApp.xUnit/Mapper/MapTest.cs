using System.Text;
using TestApp.Core.Types;
using TestApp.Domain.Entities;
using TestApp.Domain.Extensions;
using TestApp.xUnit.Mapper.Service;

namespace TestApp.xUnit.Mapper;

public class MapTest
{
    [Fact]
    public void MapToData()
    {
        Console.OutputEncoding = Encoding.UTF8;
        IList<TestData> testsDto = [];
        IDataService service = new DataService();

        for (int i = 0; i < 10000; i++)
            testsDto.Add(service.GetDataObject());

        IEnumerable<TestEntity> testsentity = testsDto.Select(test => test.TestToEntity());
        foreach(TestEntity testEntity in testsentity)
            Console.WriteLine("Тест - {0}; Название - {1}; Количество вопросов - {2}", testEntity.TestId, testEntity.TestTitle, testEntity.AsksList.Count());

        Assert.NotNull(testsentity);
    }
}