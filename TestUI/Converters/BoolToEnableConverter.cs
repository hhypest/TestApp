using System;
using System.Globalization;
using System.Windows.Data;
using TestUI.Converters.Base;

namespace TestUI.Converters;

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class BoolToEnableConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            true => false,
            false => true,
            _ => false
        };
    }
}