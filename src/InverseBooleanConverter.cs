using System.Globalization;
using System.Windows.Data;

namespace FaceMosaicSharp;

/// <summary>
/// Boolean 值反转转换器（true→false, false→true），用于 WPF 数据绑定
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }
}