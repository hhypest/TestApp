using System;
using System.Globalization;
using System.Windows.Data;
using TestApp.Converters.Base;

namespace TestApp.Converters;

[ValueConversion(typeof(int), typeof(bool))]
public sealed class CountToEnableConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            > 0 => true,
            _ => false
        };
    }
}