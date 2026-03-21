using System;
using System.Globalization;
using System.Windows.Data;

namespace EtherealBar
{
    public class TileWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 2 && values[0] is double height && values[1] is double ratio)
            {
                return height * ratio;
            }
            return 210.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return Array.Empty<object>();
        }
    }
}
