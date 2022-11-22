using NightModel.Converters.Base;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace TestApp.ViewModel.Converters;

[ValueConversion(typeof(bool), typeof(string))]
[MarkupExtensionReturnType(typeof(SaveTestConverter))]
internal class SaveTestConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool save)
            return null;

        return save switch
        {
            true => "Тест сохранен",
            false => "Тест не сохранен"
        };
    }
}