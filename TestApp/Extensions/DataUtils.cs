using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using TestApp.Models;

namespace TestApp.Extensions;

public static class DataUtils
{
    public static async Task SaveTestFile(this TestModel test, string filePath)
    {
        var streamOption = GetStreamOptions(true);
        var jsonOption = GetJsonOptions(true);

        using var stream = new FileStream(filePath, streamOption);
        await JsonSerializer.SerializeAsync(stream, test, jsonOption);
    }

    public static async Task<TestModel> LoadTestFile(string filePath)
    {
        var streamOption = GetStreamOptions(false);
        var jsonOption = GetJsonOptions(false);

        using var stream = new FileStream(filePath, streamOption);
        var test = await JsonSerializer.DeserializeAsync<TestModel>(stream, jsonOption) ?? throw new JsonException($"Невозможно открыть файл - {filePath}");

        if (test.AsksList.Count < 1 || test.AsksList[0].AnswersList.Count < 1)
            throw new JsonException($"Невозможно открыть файл - {filePath}");

        return test;
    }

    private static FileStreamOptions GetStreamOptions(bool typeStream)
    {
        return typeStream ? new()
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None
        } : new()
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read
        };
    }

    private static JsonSerializerOptions GetJsonOptions(bool typeStream)
    {
        return typeStream ? new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        } : new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
}