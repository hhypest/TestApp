using System;
using System.Globalization;
using System.Windows.Data;
using TestUI.Converters.Base;

namespace TestUI.Converters;

[ValueConversion(typeof(int), typeof(string))]
public sealed class IntToStatusConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value is not int result) ? null : result switch
        {
            0 => "Список вопросов пуст",
            _ => $"Количество вопросов - {value}"
        };
    }
}