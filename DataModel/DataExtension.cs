using System.Text.Encodings.Web;
using System.Text.Json;

namespace DataModel;

public static class DataExtension<T> where T : struct
{
    public static async Task SaveTestAsync(T test, string pathSave)
    {
        var optionStream = new FileStreamOptions()
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

        var optionJson = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        using var stream = new FileStream(pathSave, optionStream);
        await JsonSerializer.SerializeAsync(stream, test, optionJson);
    }

    public static async ValueTask<T> OpenTestAsync(string pathOpen)
    {
        var optionStream = new FileStreamOptions()
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read
        };

        var optionJson = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        using var stream = new FileStream(pathOpen, optionStream);
        return await JsonSerializer.DeserializeAsync<T>(stream, optionJson);
    }
}