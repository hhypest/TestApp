using MaterialDesignThemes.Wpf;
using System;
using System.Globalization;
using System.Windows.Data;
using TestApp.Converters.Base;

namespace TestApp.Converters;

[ValueConversion(typeof(bool), typeof(PackIconKind))]
public sealed class KindConverter : BaseConverter
{
    protected override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            true => PackIconKind.CheckOutline,
            _ => PackIconKind.CancelOutline
        };
    }
}