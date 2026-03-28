using System;
using System.Globalization;
using System.Windows.Data;

namespace EtherealBar
{
    public sealed class TileSizeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3)
                return 0d;

            // values[0] is IsVerticalDock (kept for XAML compatibility)
            double minor = values[1] is double d ? d : 0d; // minor here is always tile height
            double aspect = values[2] is double a ? a : 1d;
            aspect = Math.Max(0.01, aspect);

            string dim = (parameter as string) ?? string.Empty;
            if (string.Equals(dim, "Width", StringComparison.OrdinalIgnoreCase))
            {
                return minor * aspect;
            }
            if (string.Equals(dim, "Height", StringComparison.OrdinalIgnoreCase))
            {
                return minor;
            }

            return 0d;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
