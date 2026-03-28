using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace EtherealBar
{
    internal sealed class AccentOpacityBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return System.Windows.Media.Brushes.Transparent;

            if (values[0] is not SolidColorBrush solid)
                return System.Windows.Media.Brushes.Transparent;

            double opacity = 0.2;
            if (values[1] is double d)
                opacity = d;
            else if (values[1] is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                opacity = parsed;

            opacity = Math.Clamp(opacity, 0, 1);

            System.Windows.Media.Color c = solid.Color;
            byte a = (byte)Math.Round(255 * opacity);
            var result = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, c.R, c.G, c.B));
            result.Freeze();
            return result;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
