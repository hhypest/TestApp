using TestApp.Core.Extensions;
using TestApp.Core.Types;
using TestApp.Domain.Entities;
using TestApp.xUnit.Mapper.Service;

namespace TestApp.xUnit.Mapper;

public class MapTest
{
    [Fact]
    public void MapToData()
    {
        IDataService service = new DataService();
        TestData testData = service.GetDataObject();
        IEnumerable<AskEntity> asks = testData.AsksList.Select(a =>
        {
            IEnumerable<AnswerEntity> answers = a.AnswersList.Select(DataMapper.mapBack<AnswerEntity, AnswerData>);
            AskEntity ask = DataMapper.mapBack<AskEntity, AskData>(a);
            ask.AnswersList = answers;
            return ask;
        });

        TestEntity testEntity = DataMapper.mapBack<TestEntity, TestData>(testData);
        testEntity.AsksList = asks;
        Assert.NotNull(testEntity);
        Assert.IsType<TestEntity>(testEntity);
        Assert.NotEmpty(testEntity.AsksList);
    }
}