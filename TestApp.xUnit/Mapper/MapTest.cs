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

        TestEntity testEntity = DataMapper.mapBack<TestEntity, TestData>(testData);
        Console.WriteLine(testEntity);
        Assert.NotNull(testEntity);
        Assert.IsType<TestEntity>(testEntity);
    }
}