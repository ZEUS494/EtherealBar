using System;
using System.Globalization;
using System.Windows.Data;

namespace EtherealBar
{
    public class MediaDimensionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3)
                return 0d;

            if (values[0] is not double viewportWidth ||
                values[1] is not double viewportHeight ||
                values[2] is not double sourceAspectRatio ||
                viewportWidth <= 0 ||
                viewportHeight <= 0 ||
                sourceAspectRatio <= 0)
            {
                return 0d;
            }

            double viewportAspect = viewportWidth / viewportHeight;
            bool widthRequested = string.Equals(parameter as string, "Width", StringComparison.OrdinalIgnoreCase);

            if (sourceAspectRatio >= viewportAspect)
            {
                double filledHeight = viewportHeight;
                double filledWidth = filledHeight * sourceAspectRatio;
                return widthRequested ? filledWidth : filledHeight;
            }

            double baseWidth = viewportWidth;
            double baseHeight = baseWidth / sourceAspectRatio;
            return widthRequested ? baseWidth : baseHeight;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return Array.Empty<object>();
        }
    }
}

