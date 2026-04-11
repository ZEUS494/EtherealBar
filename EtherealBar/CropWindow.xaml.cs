using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EtherealBar
{
    public partial class CropWindow : Window
    {
        private const double SurfacePadding = 24;
        private const double ViewportCornerRadius = 22;
        private readonly ButtonConfig _config;
        private readonly ScaleTransform _scaleTransform = new ScaleTransform(1, 1);
        private readonly TranslateTransform _translateTransform = new TranslateTransform();

        private double _workingScale;
        private double _workingOffsetX;
        private double _workingOffsetY;
        private double _sourceAspectRatio = 1.0;
        private System.Windows.Point _dragStartPoint;
        private double _dragStartTranslateX;
        private double _dragStartTranslateY;
        private bool _isDragging;

        public CropWindow(ButtonConfig config)
        {
            InitializeComponent();
            _config = config;

            _workingScale = Math.Max(1.0, config.MediaScale);
            _workingOffsetX = config.MediaOffsetX;
            _workingOffsetY = config.MediaOffsetY;
            _sourceAspectRatio = Math.Max(0.01, MediaFileHelper.TryGetImageAspectRatio(config.Path) ?? config.SourceAspectRatio);

            Loaded += CropWindow_Loaded;
        }

        private bool IsVideoFile
        {
            get
            {
                return MediaFileHelper.IsVideoFile(_config.Path);
            }
        }

        private void CropWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateViewportClip();
            UpdateViewportAspect();
            UpdatePreviewElementSize();
            UpdateTransformsFromOffsets();
            ClampTranslate();
            ZoomSlider.Value = _workingScale;

            if (IsVideoFile)
            {
                PreviewVideo.Source = new Uri(_config.Path);
                PreviewVideo.Visibility = Visibility.Visible;
                PreviewVideo.RenderTransform = new TransformGroup();
                ((TransformGroup)PreviewVideo.RenderTransform).Children.Add(_scaleTransform);
                ((TransformGroup)PreviewVideo.RenderTransform).Children.Add(_translateTransform);
                UpdatePreviewElementSize();
                UpdateTransformsFromOffsets();
                PreviewVideo.Play();
            }
            else if (MediaFileHelper.IsGifFile(_config.Path))
            {
                PreviewGif.SourcePath = _config.Path;
                PreviewGif.Visibility = Visibility.Visible;
                PreviewGif.RenderTransform = new TransformGroup();
                ((TransformGroup)PreviewGif.RenderTransform).Children.Add(_scaleTransform);
                ((TransformGroup)PreviewGif.RenderTransform).Children.Add(_translateTransform);
                
                _sourceAspectRatio = Math.Max(0.01, MediaFileHelper.TryGetImageAspectRatio(_config.Path) ?? _sourceAspectRatio);
                
                UpdatePreviewElementSize();
                UpdateTransformsFromOffsets();
            }
            else
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(_config.Path);
                bitmap.EndInit();
                bitmap.Freeze();

                _sourceAspectRatio = Math.Max(0.01, MediaFileHelper.TryGetImageAspectRatio(_config.Path) ?? _sourceAspectRatio);

                PreviewImage.Source = bitmap;
                PreviewImage.Visibility = Visibility.Visible;
                PreviewImage.RenderTransform = new TransformGroup();
                ((TransformGroup)PreviewImage.RenderTransform).Children.Add(_scaleTransform);
                ((TransformGroup)PreviewImage.RenderTransform).Children.Add(_translateTransform);
                UpdatePreviewElementSize();
                UpdateTransformsFromOffsets();
            }
        }

        private void UpdateViewportAspect()
        {
            if (EditorSurface.ActualWidth <= 0 || EditorSurface.ActualHeight <= 0)
                return;

            double availableWidth = Math.Max(120, EditorSurface.ActualWidth - (SurfacePadding * 2));
            double availableHeight = Math.Max(120, EditorSurface.ActualHeight - (SurfacePadding * 2));
            double targetAspect = Math.Max(0.01, _config.AspectRatio);

            double width = availableHeight * targetAspect;
            double height = availableHeight;

            if (width > availableWidth)
            {
                width = availableWidth;
                height = width / targetAspect;
            }

            MediaViewport.Width = width;
            MediaViewport.Height = height;
            ViewportBorder.Width = width;
            ViewportBorder.Height = height;
        }

        private void UpdatePreviewElementSize()
        {
            if (_sourceAspectRatio <= 0 || ViewportBorder.ActualWidth <= 0 || ViewportBorder.ActualHeight <= 0)
                return;

            double viewportWidth = ViewportBorder.ActualWidth;
            double viewportHeight = ViewportBorder.ActualHeight;
            double viewportAspect = viewportWidth / viewportHeight;

            double baseWidth;
            double baseHeight;

            // In the crop editor we start from full-source fit (contain),
            // so horizontal images in vertical frames are not pre-cropped.
            if (_sourceAspectRatio >= viewportAspect)
            {
                baseWidth = viewportWidth;
                baseHeight = baseWidth / _sourceAspectRatio;
            }
            else
            {
                baseHeight = viewportHeight;
                baseWidth = baseHeight * _sourceAspectRatio;
            }

            PreviewImage.Width = baseWidth;
            PreviewImage.Height = baseHeight;
            PreviewVideo.Width = baseWidth;
            PreviewVideo.Height = baseHeight;
            PreviewGif.Width = baseWidth;
            PreviewGif.Height = baseHeight;
        }

        private void UpdateTransformsFromOffsets()
        {
            _scaleTransform.ScaleX = _workingScale;
            _scaleTransform.ScaleY = _workingScale;

            double overflowX = GetOverflow(true);
            double overflowY = GetOverflow(false);

            _translateTransform.X = overflowX * _workingOffsetX;
            _translateTransform.Y = overflowY * _workingOffsetY;
        }

        private void UpdateOffsetsFromTransforms()
        {
            double overflowX = GetOverflow(true);
            double overflowY = GetOverflow(false);

            _workingOffsetX = overflowX <= 0.001 ? 0 : Math.Clamp(_translateTransform.X / overflowX, -1, 1);
            _workingOffsetY = overflowY <= 0.001 ? 0 : Math.Clamp(_translateTransform.Y / overflowY, -1, 1);
        }

        private double GetOverflow(bool isX)
        {
            if (_sourceAspectRatio <= 0 || ViewportBorder.ActualWidth <= 0 || ViewportBorder.ActualHeight <= 0)
                return 0;

            FrameworkElement previewElement;
            if (PreviewImage.Visibility == Visibility.Visible) previewElement = PreviewImage;
            else if (PreviewVideo.Visibility == Visibility.Visible) previewElement = PreviewVideo;
            else previewElement = PreviewGif;

            double baseWidth = previewElement.Width;
            double baseHeight = previewElement.Height;

            if (baseWidth <= 0 || baseHeight <= 0)
                return 0;

            double viewportWidth = ViewportBorder.ActualWidth;
            double viewportHeight = ViewportBorder.ActualHeight;

            double scaledWidth = baseWidth * _workingScale;
            double scaledHeight = baseHeight * _workingScale;

            return isX
                ? Math.Max(0, (scaledWidth - viewportWidth) / 2)
                : Math.Max(0, (scaledHeight - viewportHeight) / 2);
        }

        private void ClampTranslate()
        {
            double overflowX = GetOverflow(true);
            double overflowY = GetOverflow(false);

            _translateTransform.X = Math.Clamp(_translateTransform.X, -overflowX, overflowX);
            _translateTransform.Y = Math.Clamp(_translateTransform.Y, -overflowY, overflowY);
            UpdateOffsetsFromTransforms();
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;

            _workingScale = Math.Max(1.0, ZoomSlider.Value);
            UpdateTransformsFromOffsets();
            ClampTranslate();
        }

        private void ViewportBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePreviewElementSize();
            UpdateTransformsFromOffsets();
            ClampTranslate();
        }

        private void EditorSurface_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateViewportClip();
            UpdateViewportAspect();
            UpdatePreviewElementSize();
            UpdateTransformsFromOffsets();
            ClampTranslate();
        }

        private void MediaViewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateViewportClip();
        }

        private void UpdateViewportClip()
        {
            if (MediaViewport.ActualWidth <= 0 || MediaViewport.ActualHeight <= 0)
                return;

            MediaViewport.Clip = new RectangleGeometry(
                new Rect(0, 0, MediaViewport.ActualWidth, MediaViewport.ActualHeight),
                ViewportCornerRadius,
                ViewportCornerRadius);
        }

        private void ViewportBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(EditorSurface);
            _dragStartTranslateX = _translateTransform.X;
            _dragStartTranslateY = _translateTransform.Y;
            EditorSurface.CaptureMouse();
        }

        private void ViewportBorder_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDragging) return;

            System.Windows.Point currentPoint = e.GetPosition(EditorSurface);
            Vector delta = currentPoint - _dragStartPoint;

            _translateTransform.X = _dragStartTranslateX + delta.X;
            _translateTransform.Y = _dragStartTranslateY + delta.Y;
            ClampTranslate();
        }

        private void ViewportBorder_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            EditorSurface.ReleaseMouseCapture();
        }

        private void ViewportBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double delta = e.Delta > 0 ? 0.08 : -0.08;
            ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + delta, ZoomSlider.Minimum, ZoomSlider.Maximum);
            e.Handled = true;
        }

        private void PreviewVideo_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (PreviewVideo.NaturalVideoHeight > 0)
            {
                _sourceAspectRatio = (double)PreviewVideo.NaturalVideoWidth / PreviewVideo.NaturalVideoHeight;
                _config.SourceAspectRatio = _sourceAspectRatio;
            }

            UpdatePreviewElementSize();
            UpdateTransformsFromOffsets();
            ClampTranslate();
        }

        private void PreviewVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            PreviewVideo.Position = TimeSpan.Zero;
            PreviewVideo.Play();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _config.MediaScale = _workingScale;
            _config.MediaOffsetX = _workingOffsetX;
            _config.MediaOffsetY = _workingOffsetY;
            _config.SourceAspectRatio = _sourceAspectRatio;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                PreviewVideo.Stop();
            }
            catch
            {
            }

            base.OnClosed(e);
        }
    }
}

