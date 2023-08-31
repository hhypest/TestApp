using System.Text.Encodings.Web;
using System.Text.Json;
using TestData.Models.Test;

namespace TestData.Extensions;

public static class SerializeExtension
{
    public async static ValueTask SaveJson(this TestModel test, string path)
    {
        var jsonOption = GetJsonOptions(true);
        var streamOption = GetFileStreamOptions(true);

        using var stream = new FileStream(path, streamOption);
        await JsonSerializer.SerializeAsync(stream, test, jsonOption);
    }

    public async static ValueTask<TestModel> LoadJson(string path)
    {
        var jsonOption = GetJsonOptions(false);
        var streamOption = GetFileStreamOptions(false);

        using var stream = new FileStream(path, streamOption);
        var result = await JsonSerializer.DeserializeAsync<TestModel>(stream, jsonOption);

        if (result is null || result.AsksList.Count < 1)
            throw new JsonException("Невалидный файл теста.");

        return result;
    }

    private static JsonSerializerOptions GetJsonOptions(bool state)
    {
        return state switch
        {
            true => new JsonSerializerOptions()
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            },
            false => new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }
        };
    }

    private static FileStreamOptions GetFileStreamOptions(bool state)
    {
        return state switch
        {
            true => new FileStreamOptions()
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None
            },
            false => new FileStreamOptions()
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read
            }
        };
    }
}