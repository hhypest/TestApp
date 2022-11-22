using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TestApp.ViewModel.ItemStyle;

public class AnswerSelector : StyleSelector
{
    public Style? SingleAnswerStyle { get; set; }

    public Style? MultipleAnswerStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
    {
        var parent = FindParent<ListView>(container);
        if (parent is not ListView list || list.Tag is not bool isSingle)
            return null;

        return isSingle switch
        {
            true => SingleAnswerStyle,
            false => MultipleAnswerStyle
        };
    }

    private static T? FindParent<T>(DependencyObject container) where T : FrameworkElement
    {
        var parent = VisualTreeHelper.GetParent(container);
        while (parent is not null)
        {
            if (parent is T finded)
                return finded;

            return FindParent<T>(parent);
        }

        return null;
    }
}