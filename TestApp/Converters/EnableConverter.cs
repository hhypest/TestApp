using System;
using System.Globalization;
using System.Windows.Data;
using TestApp.Converters.Base;

namespace TestApp.Converters;

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class EnableConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            true => false,
            _ => true
        };
    }
}