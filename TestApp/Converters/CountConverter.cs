using System;
using System.Globalization;
using System.Windows.Data;
using TestApp.Converters.Base;

namespace TestApp.Converters;

[ValueConversion(typeof(int), typeof(string))]
public sealed class CountConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            > 0 => $"Количество вопросов - {value}",
            _ => "Список вопросов пуст"
        };
    }
}