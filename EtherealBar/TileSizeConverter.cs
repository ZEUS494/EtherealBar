using System;
using System.Globalization;
using System.Windows.Data;

namespace EtherealBar
{
    public sealed class TileSizeConverter : IMultiValueConverter
    {
        private const double DefaultVerticalDockWidthAspect = 16d / 9d;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values: [0]=IsVerticalDock, [1]=TileHeight, [2]=HorizontalAspectRatio, [3]=VerticalHeightFactor
            if (values == null || values.Length < 4)
                return 0d;

            bool isVerticalDock = values[0] is bool b && b;
            double tileHeight = values[1] is double d ? d : 0d;
            double horizontalAspect = values[2] is double ha ? ha : 1d;
            double verticalHeightFactor = values[3] is double vh ? vh : (9d / 16d);

            tileHeight = Math.Max(1, tileHeight);
            horizontalAspect = Math.Max(0.01, horizontalAspect);
            verticalHeightFactor = Math.Clamp(verticalHeightFactor, 9d / 16d, 16d / 9d);

            double fixedVerticalWidth = tileHeight * DefaultVerticalDockWidthAspect;

            string dim = (parameter as string) ?? string.Empty;
            if (string.Equals(dim, "Width", StringComparison.OrdinalIgnoreCase))
            {
                return isVerticalDock ? fixedVerticalWidth : tileHeight * horizontalAspect;
            }
            if (string.Equals(dim, "Height", StringComparison.OrdinalIgnoreCase))
            {
                return isVerticalDock ? (fixedVerticalWidth * verticalHeightFactor) : tileHeight;
            }

            return 0d;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
