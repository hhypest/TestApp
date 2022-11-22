using NightModel.Converters.Base;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace TestApp.ViewModel.Converters;

[ValueConversion(typeof(int), typeof(string))]
[MarkupExtensionReturnType(typeof(IntStringConverter))]
internal class IntStringConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int count)
            return null;

        return count switch
        {
            > 0 => $"Количество вопросов - {count} шт.",
            _ => "Список вопросов пуст"
        };
    }
}