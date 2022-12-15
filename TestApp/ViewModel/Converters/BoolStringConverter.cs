using NightModel.Converters.Base;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace TestApp.ViewModel.Converters;

[ValueConversion(typeof(bool), typeof(string))]
[MarkupExtensionReturnType(typeof(BoolStringConverter))]
internal class BoolStringConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool state)
            return null;

        return state switch
        {
            true => "Да",
            false => "Нет"
        };
    }
}