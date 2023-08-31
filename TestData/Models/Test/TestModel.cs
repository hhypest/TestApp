using TestData.Models.Ask;

namespace TestData.Models.Test;

public record class TestModel
{
    public string TitleTest { get; init; }

    public List<AskModel> AsksList { get; init; }

    public TestModel(string titleTest, List<AskModel> asksList)
    {
        TitleTest = titleTest;
        AsksList = asksList;
    }
}