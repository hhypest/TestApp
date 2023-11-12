using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TestApp.Converters.Base;

namespace TestApp.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            true => Visibility.Visible,
            _ => Visibility.Collapsed
        };
    }
}