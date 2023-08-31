using System;
using System.Globalization;
using System.Windows.Data;
using TestUI.Converters.Base;

namespace TestUI.Converters;

[ValueConversion(typeof(string), typeof(string))]
public sealed class StringToStatusConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value is not string result) ? null : string.IsNullOrWhiteSpace(result) switch
        {
            true => "Тест не сохранен!",
            false => $"Тест сохранен по пути - {result}"
        };
    }
}