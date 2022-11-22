using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TestApp.Service.ErrorsMessage;

public class ErrorsMessage : IErrorsMessage
{
    private readonly IDictionary<string, string> _errors;

    public ErrorsMessage()
    {
        var optionJson = new JsonSerializerOptions()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var optionStream = new FileStreamOptions()
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
        };

        var path = @$"{Directory.GetCurrentDirectory()}\errors\errors.json";
        using var stream = new FileStream(path, optionStream);
        var result = JsonSerializer.Deserialize<IDictionary<string, string>>(stream, optionJson);
        _errors = result ?? new Dictionary<string, string>();
    }

    public string GetError(string propertyName)
    {
        if (_errors.TryGetValue(propertyName, out var value) && value is not null)
            return value;

        _errors.Add(propertyName, $"В свойстве <{propertyName}> возникла неизвестная ошибка!");
        return _errors[propertyName];
    }
}