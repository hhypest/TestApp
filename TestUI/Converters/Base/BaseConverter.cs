using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace TestUI.Converters.Base;

[MarkupExtensionReturnType(typeof(BaseConverter))]
public abstract class BaseConverter : MarkupExtension, IValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    protected abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    protected virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Обратное преобразование не поддерживается");

    object? IValueConverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Convert(value, targetType, parameter, culture);

    object? IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConvertBack(value, targetType, parameter, culture);
}