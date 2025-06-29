using TestApp.Core.Types;
using TestApp.Domain.Entities;

namespace TestApp.xUnit.Mapper.Service;

internal interface IDataService
{
    internal TestData GetDataObject();
    internal TestEntity GetEntity();
}