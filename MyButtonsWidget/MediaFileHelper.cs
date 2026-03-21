using System;
using System.IO;
using Drawing = System.Drawing;

namespace MyButtonsWidget
{
    internal static class MediaFileHelper
    {
        public static bool IsVideoFile(string? path)
        {
            string extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return extension is ".mp4" or ".avi" or ".mov" or ".mkv";
        }

        public static double? TryGetImageAspectRatio(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || IsVideoFile(path))
                return null;

            try
            {
                using Drawing.Image image = Drawing.Image.FromFile(path);
                int width = image.Width;
                int height = image.Height;
                if (width <= 0 || height <= 0)
                    return null;

                const int ExifOrientationId = 0x0112;
                if (Array.IndexOf(image.PropertyIdList, ExifOrientationId) >= 0)
                {
                    Drawing.Imaging.PropertyItem? orientationItem = image.GetPropertyItem(ExifOrientationId);
                    byte[]? orientationBytes = orientationItem?.Value;
                    ushort orientation = orientationBytes is { Length: >= 2 }
                        ? BitConverter.ToUInt16(orientationBytes, 0)
                        : (ushort)1;

                    if (orientation is 5 or 6 or 7 or 8)
                    {
                        (width, height) = (height, width);
                    }
                }

                return (double)width / height;
            }
            catch
            {
                return null;
            }
        }
    }
}
