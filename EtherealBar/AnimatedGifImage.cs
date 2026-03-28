using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;

namespace EtherealBar
{
    /// <summary>
    /// Lightweight animated GIF support for WPF without external packages.
    /// Shows first frame for non-GIF files.
    /// </summary>
    public sealed class AnimatedGifImage : System.Windows.Controls.Image
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public static readonly DependencyProperty SourcePathProperty =
            DependencyProperty.Register(
                nameof(SourcePath),
                typeof(string),
                typeof(AnimatedGifImage),
                new PropertyMetadata(null, OnSourcePathChanged));

        public string? SourcePath
        {
            get => (string?)GetValue(SourcePathProperty);
            set => SetValue(SourcePathProperty, value);
        }

        private readonly DispatcherTimer _timer;
        private List<BitmapSource>? _frames;
        private List<TimeSpan>? _delays;
        private int _frameIndex;

        public AnimatedGifImage()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render);
            _timer.Tick += Timer_Tick;

            Loaded += (_, _) => TryStart();
            Unloaded += (_, _) => Stop();
            IsVisibleChanged += (_, _) =>
            {
                if (!IsVisible) Stop();
                else TryStart();
            };
        }

        private static void OnSourcePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AnimatedGifImage img) return;
            img.Reload();
        }

        private void Reload()
        {
            Stop();
            _frames = null;
            _delays = null;
            _frameIndex = 0;
            Source = null;

            string? path = SourcePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            if (!MediaFileHelper.IsGifFile(path))
            {
                // Non-GIF: this control is expected to be collapsed, so don't load.
                return;
            }

            try
            {
                LoadGifFrames(path, out _frames, out _delays);
                if (_frames != null && _frames.Count > 0)
                {
                    Source = _frames[0];
                    TryStart();
                }
            }
            catch
            {
                _frames = null;
                _delays = null;
                Source = null;
            }
        }

        private void TryStart()
        {
            if (!IsLoaded || !IsVisible) return;
            if (_frames == null || _delays == null || _frames.Count <= 1) return;
            if (_timer.IsEnabled) return;

            _frameIndex = Math.Clamp(_frameIndex, 0, _frames.Count - 1);
            _timer.Interval = SafeDelay(_delays[_frameIndex]);
            _timer.Start();
        }

        private void Stop()
        {
            if (_timer.IsEnabled)
                _timer.Stop();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_frames == null || _delays == null || _frames.Count == 0)
            {
                Stop();
                return;
            }

            _frameIndex = (_frameIndex + 1) % _frames.Count;
            Source = _frames[_frameIndex];
            _timer.Interval = SafeDelay(_delays[_frameIndex]);
        }

        private static TimeSpan SafeDelay(TimeSpan delay)
        {
            // Some GIFs have 0ms frames, which would spin the UI thread.
            if (delay < TimeSpan.FromMilliseconds(16))
                return TimeSpan.FromMilliseconds(60);
            return delay;
        }

        private static void LoadGifFrames(string path, out List<BitmapSource> frames, out List<TimeSpan> delays)
        {
            frames = new List<BitmapSource>();
            delays = new List<TimeSpan>();

            using Drawing.Image gif = Drawing.Image.FromFile(path);
            if (gif.FrameDimensionsList == null || gif.FrameDimensionsList.Length == 0)
                return;

            var dimension = new DrawingImaging.FrameDimension(gif.FrameDimensionsList[0]);
            int count = gif.GetFrameCount(dimension);
            if (count <= 0)
                return;

            int[] delayCs = ReadFrameDelaysCentiseconds(gif, count);

            for (int i = 0; i < count; i++)
            {
                gif.SelectActiveFrame(dimension, i);
                using var bmp = new Drawing.Bitmap(gif);
                IntPtr hBitmap = bmp.GetHbitmap();
                try
                {
                    BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    frames.Add(src);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }

                int cs = (i < delayCs.Length) ? delayCs[i] : 10;
                if (cs <= 0) cs = 10;
                delays.Add(TimeSpan.FromMilliseconds(cs * 10));
            }
        }

        private static int[] ReadFrameDelaysCentiseconds(Drawing.Image image, int frameCount)
        {
            const int PropertyTagFrameDelay = 0x5100;
            try
            {
                if (Array.IndexOf(image.PropertyIdList, PropertyTagFrameDelay) < 0)
                    return DefaultDelays(frameCount);

                DrawingImaging.PropertyItem? item = image.GetPropertyItem(PropertyTagFrameDelay);
                byte[] values = item?.Value ?? Array.Empty<byte>();

                // Each delay is a 4-byte integer, in 1/100 sec.
                int expectedBytes = frameCount * 4;
                if (values.Length < expectedBytes)
                    return DefaultDelays(frameCount);

                int[] delays = new int[frameCount];
                for (int i = 0; i < frameCount; i++)
                {
                    delays[i] = BitConverter.ToInt32(values, i * 4);
                }
                return delays;
            }
            catch
            {
                return DefaultDelays(frameCount);
            }
        }

        private static int[] DefaultDelays(int frameCount)
        {
            int[] delays = new int[frameCount];
            for (int i = 0; i < frameCount; i++)
                delays[i] = 10; // 100ms
            return delays;
        }
    }
}
