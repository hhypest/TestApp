using System;
using System.Globalization;
using System.Windows.Data;
using TestUI.Converters.Base;

namespace TestUI.Converters;

[ValueConversion(typeof(int), typeof(bool))]
public sealed class IntToEnableConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int count)
            return null;

        return count switch
        {
            > 0 => true,
            _ => false
        };
    }
}