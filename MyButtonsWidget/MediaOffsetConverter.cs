using System;
using System.Globalization;
using System.Windows.Data;

namespace MyButtonsWidget
{
    public class MediaOffsetConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 5)
                return 0d;

            if (values[0] is not double offsetRatio ||
                values[1] is not double viewportWidth ||
                values[2] is not double viewportHeight ||
                values[3] is not double mediaScale ||
                values[4] is not double sourceAspectRatio ||
                viewportWidth <= 0 ||
                viewportHeight <= 0 ||
                sourceAspectRatio <= 0)
            {
                return 0d;
            }

            double viewportAspect = viewportWidth / viewportHeight;
            double baseWidth;
            double baseHeight;

            if (sourceAspectRatio >= viewportAspect)
            {
                baseHeight = viewportHeight;
                baseWidth = baseHeight * sourceAspectRatio;
            }
            else
            {
                baseWidth = viewportWidth;
                baseHeight = baseWidth / sourceAspectRatio;
            }

            double scaledWidth = baseWidth * mediaScale;
            double scaledHeight = baseHeight * mediaScale;

            bool isX = string.Equals(parameter as string, "X", StringComparison.OrdinalIgnoreCase);
            double overflow = isX
                ? Math.Max(0, (scaledWidth - viewportWidth) / 2)
                : Math.Max(0, (scaledHeight - viewportHeight) / 2);

            return offsetRatio * overflow;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return Array.Empty<object>();
        }
    }
}
