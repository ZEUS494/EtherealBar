using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Newtonsoft.Json;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Application = System.Windows.Application;

namespace EtherealBar
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess);
        private const int HOTKEY_ID = 9000;

        public ObservableCollection<ButtonConfig> Buttons { get; set; } = new ObservableCollection<ButtonConfig>();

        // РСЃРїРѕР»СЊР·СѓРµРј Р°Р±СЃРѕР»СЋС‚РЅС‹Р№ РїСѓС‚СЊ, С‡С‚РѕР±С‹ Р°РІС‚РѕР·Р°РіСЂСѓР·РєР° РЅРµ С‚РµСЂСЏР»Р° С„Р°Р№Р»
        private string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        private bool _isEditMode = false;
        private bool _isPanelVisible = false;
        private bool _isInternalShutdown = false; // Р¤Р»Р°Рі РґР»СЏ РїРѕР»РЅРѕРіРѕ РІС‹С…РѕРґР°
        private Brush _globalBorderBrush = Brushes.Cyan;
        private Color _panelBackgroundColor = Color.FromRgb(5, 5, 5);
        private Brush _panelBackgroundBrush = Brushes.Transparent;
        private bool _isExiting = false;

        private const double BaseTileHeight = 373;
        private const double MinTileHeight = 120;
        private const double TileVerticalPadding = 40;

        private double _widgetHeight = 400;
        private double _tileScale = 1.0;
        private double _panelBackgroundOpacity = 0.82;
        private double _hiddenOffset = 450;
        public double MaxWidgetHeight { get; }

        private double _targetOffset = 0;
        private double _currentOffset = 0;
        private System.Windows.Point _dragStartPoint;
        private System.Windows.Point _dragGrabOffset;
        private bool _isTileDragActive;
        private ButtonConfig? _draggedButtonConfig;
        private Button? _draggedTileButton;
        private SettingsWindow? _settingsWindow;
        private Forms.NotifyIcon? _notifyIcon;

        public bool IsEditMode { get => _isEditMode; set { _isEditMode = value; OnPropertyChanged(nameof(IsEditMode)); } }
        public Brush GlobalBorderBrush { get => _globalBorderBrush; set { _globalBorderBrush = value; OnPropertyChanged(nameof(GlobalBorderBrush)); } }
        public Color PanelBackgroundColor
        {
            get => _panelBackgroundColor;
            set
            {
                if (_panelBackgroundColor == value) return;
                _panelBackgroundColor = value;
                UpdatePanelBackgroundBrush();
                OnPropertyChanged(nameof(PanelBackgroundColor));
            }
        }
        public Brush PanelBackgroundBrush { get => _panelBackgroundBrush; private set { _panelBackgroundBrush = value; OnPropertyChanged(nameof(PanelBackgroundBrush)); } }

        public double WidgetHeight
        {
            get => _widgetHeight;
            set
            {
                double clamped = Clamp(value, 200, MaxWidgetHeight);
                if (Math.Abs(_widgetHeight - clamped) < 0.1) return;
                _widgetHeight = clamped;
                OnPropertyChanged(nameof(WidgetHeight));
                OnPropertyChanged(nameof(TileWidth));
                OnPropertyChanged(nameof(TileHeight));
                UpdateLayoutMetrics();
            }
        }

        public double TileScale
        {
            get => _tileScale;
            set
            {
                double clamped = Clamp(value, 0.70, 1.30);
                if (Math.Abs(_tileScale - clamped) < 0.0001) return;
                _tileScale = clamped;
                OnPropertyChanged(nameof(TileScale));
                OnPropertyChanged(nameof(TileWidth));
                OnPropertyChanged(nameof(TileHeight));
            }
        }

        public double PanelBackgroundOpacity
        {
            get => _panelBackgroundOpacity;
            set
            {
                double clamped = Clamp(value, 0.05, 1.0);
                if (Math.Abs(_panelBackgroundOpacity - clamped) < 0.0001) return;
                _panelBackgroundOpacity = clamped;
                UpdatePanelBackgroundBrush();
                OnPropertyChanged(nameof(PanelBackgroundOpacity));
            }
        }

        public double TileWidth => TileHeight * (210d / BaseTileHeight);
        public double TileHeight
        {
            get
            {
                double scaledHeight = BaseTileHeight * TileScale;
                double availableHeight = Math.Max(MinTileHeight, WidgetHeight - TileVerticalPadding);
                return Math.Min(scaledHeight, availableHeight);
            }
        }
        public double HiddenOffset => _hiddenOffset;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Application.Current.Exit += OnAppExit;
            Application.Current.SessionEnding += App_SessionEnding;
            new WindowInteropHelper(this).EnsureHandle();

            this.Width = SystemParameters.PrimaryScreenWidth;
            this.Left = 0;
            MaxWidgetHeight = SystemParameters.WorkArea.Height / 2.0;
            UpdatePanelBackgroundBrush();
            UpdateLayoutMetrics();

            InitNotifyIcon();
            LoadSettings();
            SetAutostart(true);
            this.Visibility = Visibility.Hidden;
        }

        private void SetAutostart(bool enable)
        {
            try
            {
                string path = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                RegistryKey? rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (enable) rk?.SetValue("EtherealBar", path);
                else rk?.DeleteValue("EtherealBar", false);
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isInternalShutdown)
            {
                ForceExit();
                return;
            }

            e.Cancel = true;
            TogglePanel();
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void UpdatePanelBackgroundBrush()
        {
            byte alpha = (byte)Math.Round(255 * PanelBackgroundOpacity);
            PanelBackgroundBrush = new SolidColorBrush(Color.FromArgb(alpha, PanelBackgroundColor.R, PanelBackgroundColor.G, PanelBackgroundColor.B));
        }

        private void UpdateLayoutMetrics()
        {
            _hiddenOffset = WidgetHeight + 50;
            OnPropertyChanged(nameof(HiddenOffset));

            this.Height = WidgetHeight + 100;
            this.Top = SystemParameters.WorkArea.Bottom - this.Height;

            if (!_isPanelVisible)
            {
                PanelTransform.Y = HiddenOffset;
            }
        }

        private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            _isInternalShutdown = true;

            // e.Cancel = false; // РЈСЃС‚Р°РЅР°РІР»РёРІР°С‚СЊ РЅРµ РЅСѓР¶РЅРѕ, РѕРЅРѕ false РїРѕ СѓРјРѕР»С‡Р°РЅРёСЋ

            ForceExit();

            try
            {
                SaveSettings();
            }
            catch { }
        }
        private bool IsAutostartEnabled()
        {
            try
            {
                using RegistryKey? rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                return rk?.GetValue("EtherealBar") != null;
            }
            catch { return false; }
        }
        private void InitNotifyIcon()
        {
            _notifyIcon = new Forms.NotifyIcon();
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            _notifyIcon.Text = "EtherealBar";
            _notifyIcon.Visible = true;

            var contextMenu = new Forms.ContextMenuStrip();

            var autostartItem = new Forms.ToolStripMenuItem("Запускать вместе с Windows");
            autostartItem.CheckOnClick = true;
            autostartItem.Checked = IsAutostartEnabled();
            autostartItem.Click += (s, e) => SetAutostart(autostartItem.Checked);

            contextMenu.Items.Add(autostartItem);
            contextMenu.Items.Add(new Forms.ToolStripSeparator());
            contextMenu.Items.Add("Показать/Скрыть", null, (s, e) => TogglePanel());
            contextMenu.Items.Add("Выход", null, (s, e) => {
                _isInternalShutdown = true;
                Application.Current.Shutdown();
            });

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void TogglePanel()
        {
            if (_isInternalShutdown) return;
            _isPanelVisible = !_isPanelVisible;
            double targetY = _isPanelVisible ? 0 : HiddenOffset;
            DoubleAnimation anim = new DoubleAnimation(targetY, TimeSpan.FromSeconds(0.4)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };

            if (_isPanelVisible)
            {
                this.Show();
                this.Opacity = 0;
                this.Activate();
                FocusManager.SetFocusedElement(this, MainScrollViewer);
                this.Topmost = true;

                ManageAllMedia(true);
                CompositionTarget.Rendering += OnRenderFrame;
                PanelTransform.BeginAnimation(TranslateTransform.YProperty, anim);

                var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.12)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                this.BeginAnimation(OpacityProperty, fadeIn);
            }
            else
            {
                if (_settingsWindow != null)
                {
                    try { _settingsWindow.Close(); } catch { }
                    _settingsWindow = null;
                }

                var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.12))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                };
                this.BeginAnimation(OpacityProperty, fadeOut);

                anim.Completed += (s, e) => {
                    if (!_isPanelVisible)
                    {
                        CompositionTarget.Rendering -= OnRenderFrame;
                        ManageAllMedia(false);
                        RunDeepCleanup();
                        this.Hide();
                        SaveSettings();
                    }
                };
                PanelTransform.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }

        private void ManageAllMedia(bool play)
        {
            for (int i = 0; i < MediaHost.Items.Count; i++)
            {
                var container = MediaHost.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                var media = FindVisualChild<MediaElement>(container);
                if (media == null) continue;
                if (play) { media.Visibility = Visibility.Visible; media.Play(); }
                else { media.Pause(); }
            }
        }

        private void ForceExit()
        {
            if (_isExiting) return;
            _isExiting = true;

            try
            {
                // Р—Р°РјРµРЅСЏРµРј Invoke РЅР° Р°СЃРёРЅС…СЂРѕРЅРЅС‹Р№ BeginInvoke, С‡С‚РѕР±С‹ РёР·Р±РµР¶Р°С‚СЊ Р»СЋР±С‹С… Р±Р»РѕРєРёСЂРѕРІРѕРє РїСЂРё РІС‹РєР»СЋС‡РµРЅРёРё
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(ForceExit));
                    return;
                }

                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                // РћСЃРІРѕР±РѕР¶РґР°РµРј РіРѕСЂСЏС‡СѓСЋ РєР»Р°РІРёС€Сѓ
                IntPtr h = new WindowInteropHelper(this).Handle;
                UnregisterHotKey(h, HOTKEY_ID);

                // Р•СЃР»Рё РїРѕРґРїРёСЃС‹РІР°Р»РёСЃСЊ РЅР° Application.Current.SessionEnding, РѕС‚РїРёСЃС‹РІР°С‚СЊСЃСЏ РЅРµ РѕР±СЏР·Р°С‚РµР»СЊРЅРѕ, 
                // С‚Р°Рє РєР°Рє РїСЂРёР»РѕР¶РµРЅРёРµ РІСЃС‘ СЂР°РІРЅРѕ СѓРЅРёС‡С‚РѕР¶Р°РµС‚СЃСЏ, РЅРѕ РґР»СЏ С‡РёСЃС‚РѕС‚С‹ РјРѕР¶РЅРѕ РґРѕР±Р°РІРёС‚СЊ:
                Application.Current.SessionEnding -= App_SessionEnding;
            }
            catch { }
        }

        private void RunDeepCleanup()
        {
            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            try { EmptyWorkingSet(Process.GetCurrentProcess().Handle); } catch { }
        }

        private void OnRenderFrame(object? sender, EventArgs e)
        {
            if (Math.Abs(_currentOffset - _targetOffset) > 0.1)
            {
                _currentOffset += (_targetOffset - _currentOffset) * 0.08;
                MainScrollViewer.ScrollToHorizontalOffset(_currentOffset);
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _targetOffset = Math.Max(0, Math.Min(MainScrollViewer.ScrollableWidth, _targetOffset - (e.Delta / 120.0) * 250));
            e.Handled = true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr h = new WindowInteropHelper(this).Handle;
            RegisterHotKey(h, HOTKEY_ID, 0x0001, 0x31);
            HwndSource.FromHwnd(h).AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) => {
                if (msg == 0x0312 && wp.ToInt32() == HOTKEY_ID) { TogglePanel(); handled = true; }
                return IntPtr.Zero;
            });
        }

        private void LoadSettings()
        {
            if (File.Exists(settingsFile)) try
                {
                    var s = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(settingsFile));
                    if (s != null)
                    {
                        string accentHex = !string.IsNullOrWhiteSpace(s.AccentColorHex) ? s.AccentColorHex : s.HoverColorHex;
                        GlobalBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentHex));
                        if (!string.IsNullOrWhiteSpace(s.PanelBackgroundColorHex))
                        {
                            PanelBackgroundColor = (Color)ColorConverter.ConvertFromString(s.PanelBackgroundColorHex);
                        }
                        TileScale = s.TileScale;
                        WidgetHeight = s.WidgetHeight;
                        PanelBackgroundOpacity = s.PanelBackgroundOpacity;
                        Buttons.Clear();
                        foreach (var b in s.Buttons) Buttons.Add(b);
                    }
                }
                catch { }

            if (Buttons.Count == 0) Buttons.Add(new ButtonConfig { Title = "Desktop" });

            foreach (var button in Buttons)
            {
                double? imageAspectRatio = MediaFileHelper.TryGetImageAspectRatio(button.Path);
                if (imageAspectRatio.HasValue)
                {
                    button.SourceAspectRatio = imageAspectRatio.Value;
                }

                if (button.IsVideoVisible == Visibility.Visible && !string.IsNullOrEmpty(button.Path))
                {
                    try
                    {
                        var tempMedia = new MediaElement { Source = new Uri(button.Path), LoadedBehavior = MediaState.Manual };
                        tempMedia.Play();
                        tempMedia.Stop();
                    }
                    catch { }
                }
            }
        }

        public void SaveSettings()
        {
            try
            {
                var settings = new AppSettings
                {
                    AccentColorHex = GlobalBorderBrush.ToString(),
                    HoverColorHex = GlobalBorderBrush.ToString(),
                    PanelBackgroundColorHex = PanelBackgroundColor.ToString(),
                    WidgetHeight = WidgetHeight,
                    TileScale = TileScale,
                    PanelBackgroundOpacity = PanelBackgroundOpacity,
                    Buttons = Buttons.ToList()
                };
                string? dir = Path.GetDirectoryName(settingsFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(settingsFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch { }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPanelVisible) return;
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow { Owner = this, DataContext = this };
                _settingsWindow.Closed += (_, __) => _settingsWindow = null;
            }
            _settingsWindow.Left = this.Left + 50;
            _settingsWindow.Top = Math.Max(0, this.Top - _settingsWindow.Height - 10);
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        private void Tile_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditMode && sender is Button b && b.DataContext is ButtonConfig c)
            {
                try { Process.Start(new ProcessStartInfo(c.AppPath) { UseShellExecute = true }); TogglePanel(); } catch { }
            }
        }

        private void ToggleEditMode_Click(object sender, RoutedEventArgs e) { IsEditMode = !IsEditMode; if (!IsEditMode) SaveSettings(); }
        private void Close_Click(object sender, RoutedEventArgs e) { if (_isPanelVisible) TogglePanel(); }
        private void AddButton_Click(object sender, RoutedEventArgs e) { Buttons.Add(new ButtonConfig { Title = "New" }); }
        private void DeleteButton_Click(object sender, RoutedEventArgs e) { if (sender is Button b && b.Tag is ButtonConfig c) Buttons.Remove(c); }
        private void SetupMedia_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Media files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.avi;*.mov;*.mkv|All files|*.*"
            };

            if (dialog.ShowDialog() != true || sender is not Button button || button.Tag is not ButtonConfig config)
                return;

            string previousPath = config.Path;
            double previousScale = config.MediaScale;
            double previousOffsetX = config.MediaOffsetX;
            double previousOffsetY = config.MediaOffsetY;
            double previousSourceAspect = config.SourceAspectRatio;

            config.Path = dialog.FileName;
            config.MediaScale = 1.0;
            config.MediaOffsetX = 0;
            config.MediaOffsetY = 0;
            config.SourceAspectRatio = MediaFileHelper.TryGetImageAspectRatio(dialog.FileName) ?? previousSourceAspect;

            if (!OpenCropWindow(config))
            {
                config.Path = previousPath;
                config.MediaScale = previousScale;
                config.MediaOffsetX = previousOffsetX;
                config.MediaOffsetY = previousOffsetY;
                config.SourceAspectRatio = previousSourceAspect;
            }
            else
            {
                SaveSettings();
            }
        }
        private void SetupApp_Click(object sender, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog();
            if (d.ShowDialog() == true && sender is Button b && b.Tag is ButtonConfig c)
            {
                c.AppPath = d.FileName;
                SaveSettings();
            }
        }
        private void CropMedia_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not ButtonConfig config || string.IsNullOrWhiteSpace(config.Path) || !File.Exists(config.Path))
                return;

            config.SourceAspectRatio = MediaFileHelper.TryGetImageAspectRatio(config.Path) ?? config.SourceAspectRatio;

            if (OpenCropWindow(config))
            {
                SaveSettings();
            }
        }
        private void bgVideo_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (sender is MediaElement mediaElement && mediaElement.DataContext is ButtonConfig config && mediaElement.NaturalVideoHeight > 0)
            {
                config.SourceAspectRatio = (double)mediaElement.NaturalVideoWidth / mediaElement.NaturalVideoHeight;
            }
        }
        private void bgVideo_MediaEnded(object? sender, RoutedEventArgs e) { if (sender is MediaElement me) { me.Position = TimeSpan.Zero; me.Play(); } }

        private bool OpenCropWindow(ButtonConfig config)
        {
            var cropWindow = new CropWindow(config)
            {
                Owner = this
            };

            cropWindow.Left = this.Left + 40;
            cropWindow.Top = Math.Max(0, this.Top - 40);
            return cropWindow.ShowDialog() == true;
        }

        private void Tile_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!IsEditMode) return;
            if (sender is Button tileButton && IsInteractiveChildClick(e.OriginalSource as DependencyObject, tileButton)) return;

            _dragStartPoint = e.GetPosition(this);
            _isTileDragActive = false;

            if (sender is Button b && b.DataContext is ButtonConfig cfg)
            {
                _draggedButtonConfig = cfg;
                _draggedTileButton = b;
                _dragGrabOffset = e.GetPosition(b);
            }
        }

        private void Tile_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!IsEditMode || e.LeftButton != MouseButtonState.Pressed || _draggedButtonConfig == null) return;
            if (sender is Button tileButton && IsInteractiveChildClick(e.OriginalSource as DependencyObject, tileButton)) return;

            System.Windows.Point currentPos = e.GetPosition(this);
            Vector diff = currentPos - _dragStartPoint;
            if (!_isTileDragActive)
            {
                if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                _isTileDragActive = true;
                _draggedButtonConfig.IsDragging = true;
                _draggedTileButton ??= sender as Button;
                _draggedTileButton?.CaptureMouse();
            }

            UpdateDraggedTilePosition(currentPos);
            UpdateDraggedTileVisualOffset(currentPos);
        }

        private void Tile_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            FinishTileDrag();
        }

        private void Tile_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isTileDragActive)
            {
                FinishTileDrag();
            }
        }

        private void FinishTileDrag()
        {
            bool shouldSave = _isTileDragActive;

            if (_draggedButtonConfig != null)
            {
                _draggedButtonConfig.DragOffsetX = 0;
                _draggedButtonConfig.DragOffsetY = 0;
                _draggedButtonConfig.IsDragging = false;
            }

            foreach (var button in Buttons)
            {
                button.IsDropTarget = false;
            }

            _isTileDragActive = false;

            if (shouldSave)
            {
                SaveSettings();
            }

            _draggedTileButton?.ReleaseMouseCapture();
            _draggedTileButton = null;
            _draggedButtonConfig = null;
        }

        private void UpdateDraggedTilePosition(System.Windows.Point currentPos)
        {
            if (_draggedButtonConfig == null)
            {
                return;
            }

            int oldIndex = Buttons.IndexOf(_draggedButtonConfig);
            if (oldIndex < 0)
            {
                return;
            }

            int rawInsertIndex = Buttons.Count;
            for (int i = 0; i < Buttons.Count; i++)
            {
                ButtonConfig candidate = Buttons[i];
                if (ReferenceEquals(candidate, _draggedButtonConfig))
                {
                    continue;
                }

                var presenter = MediaHost.ItemContainerGenerator.ContainerFromItem(candidate) as ContentPresenter;
                if (presenter == null)
                {
                    continue;
                }

                Rect bounds = presenter.TransformToAncestor(this)
                    .TransformBounds(new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight));

                if (currentPos.X < bounds.Left + (bounds.Width / 2))
                {
                    rawInsertIndex = i;
                    break;
                }
            }

            int newIndex = rawInsertIndex;
            if (newIndex > oldIndex)
            {
                newIndex--;
            }

            newIndex = Math.Clamp(newIndex, 0, Buttons.Count - 1);
            if (newIndex == oldIndex)
            {
                return;
            }

            Buttons.Move(oldIndex, newIndex);
            UpdateLayout();
            UpdateDraggedTileVisualOffset(currentPos);
        }

        private void UpdateDraggedTileVisualOffset(System.Windows.Point currentPos)
        {
            if (_draggedButtonConfig == null)
            {
                return;
            }

            Button? tileButton = _draggedTileButton;
            if (tileButton == null)
            {
                return;
            }

            Rect bounds = tileButton.TransformToAncestor(this)
                .TransformBounds(new Rect(0, 0, tileButton.ActualWidth, tileButton.ActualHeight));

            _draggedButtonConfig.DragOffsetX = currentPos.X - (bounds.Left + _dragGrabOffset.X);
            _draggedButtonConfig.DragOffsetY = currentPos.Y - (bounds.Top + _dragGrabOffset.Y);
        }

        private void TileContentBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is not Border border) return;
            double radius = border.CornerRadius.TopLeft;
            border.Clip = new RectangleGeometry(new Rect(0, 0, border.ActualWidth, border.ActualHeight), radius, radius);
        }

        private T? FindVisualChild<T>(DependencyObject? obj) where T : DependencyObject
        {
            if (obj == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child is T t) return t;
                T? res = FindVisualChild<T>(child);
                if (res != null) return res;
            }
            return null;
        }

        private void OnProcessExit(object? sender, EventArgs e)
        {
            ForceExit();
        }

        private void OnAppExit(object? sender, ExitEventArgs e)
        {
            ForceExit();
        }
        private static bool IsInteractiveChildClick(DependencyObject? originalSource, Button tileButton)
        {
            if (originalSource == null) return false;
            if (FindAncestor<System.Windows.Controls.TextBox>(originalSource, tileButton) != null) return true;
            if (FindAncestor<Slider>(originalSource, tileButton) != null) return true;
            if (FindAncestor<System.Windows.Controls.Primitives.Thumb>(originalSource, tileButton) != null) return true;
            var innerButton = FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(originalSource, tileButton);
            return innerButton != null && !ReferenceEquals(innerButton, tileButton);
        }

        private static T? FindAncestor<T>(DependencyObject? from, DependencyObject stopAt) where T : DependencyObject
        {
            DependencyObject? current = from;
            while (current != null && !ReferenceEquals(current, stopAt))
            {
                if (current is T t) return t;
                DependencyObject? parent = (current is FrameworkElement fe) ? fe.Parent : (current is FrameworkContentElement fce) ? fce.Parent : null;
                parent ??= VisualTreeHelper.GetParent(current);
                current = parent;
            }
            return null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class AppSettings
    {
        public string AccentColorHex { get; set; } = "#FF00FFFF";
        public string HoverColorHex { get; set; } = "#FF00FFFF";
        public string PanelBackgroundColorHex { get; set; } = "#FF050505";
        public double WidgetHeight { get; set; } = 400;
        public double TileScale { get; set; } = 1.0;
        public double PanelBackgroundOpacity { get; set; } = 0.82;
        public List<ButtonConfig> Buttons { get; set; } = new List<ButtonConfig>();
    }

    public class ButtonConfig : INotifyPropertyChanged
    {
        private const double MinAspect = 9d / 16d;
        private const double MaxAspect = 16d / 9d;
        private string _p = "";
        private string _t = "New";
        private bool _isDragging;
        private bool _isDropTarget;
        private double _aspectRatio = MinAspect;
        private double _mediaScale = 1.0;
        private double _mediaOffsetX;
        private double _mediaOffsetY;
        private double _sourceAspectRatio = 1.0;
        private double _dragOffsetX;
        private double _dragOffsetY;

        public string AppPath { get; set; } = "explorer.exe";
        public string Title { get => _t; set { _t = value; OnPropertyChanged(nameof(Title)); } }
        public string Path { get => _p; set { _p = value; OnPropertyChanged(nameof(Path)); OnPropertyChanged(nameof(IsVideoVisible)); OnPropertyChanged(nameof(IsImageVisible)); } }
        public double MediaScale { get => _mediaScale; set { _mediaScale = Math.Max(1.0, value); OnPropertyChanged(nameof(MediaScale)); } }
        public double MediaOffsetX { get => _mediaOffsetX; set { _mediaOffsetX = Math.Clamp(value, -1, 1); OnPropertyChanged(nameof(MediaOffsetX)); } }
        public double MediaOffsetY { get => _mediaOffsetY; set { _mediaOffsetY = Math.Clamp(value, -1, 1); OnPropertyChanged(nameof(MediaOffsetY)); } }
        public double SourceAspectRatio { get => _sourceAspectRatio; set { _sourceAspectRatio = value > 0 ? value : 1.0; OnPropertyChanged(nameof(SourceAspectRatio)); } }
        [JsonIgnore] public double DragOffsetX { get => _dragOffsetX; set { _dragOffsetX = value; OnPropertyChanged(nameof(DragOffsetX)); } }
        [JsonIgnore] public double DragOffsetY { get => _dragOffsetY; set { _dragOffsetY = value; OnPropertyChanged(nameof(DragOffsetY)); } }

        public double AspectRatio
        {
            get => _aspectRatio;
            set
            {
                double clamped = Math.Clamp(value, MinAspect, MaxAspect);
                if (Math.Abs(_aspectRatio - clamped) < 0.0001) return;
                _aspectRatio = clamped;
                _mediaOffsetX = 0;
                _mediaOffsetY = 0;
                OnPropertyChanged(nameof(AspectRatio));
                OnPropertyChanged(nameof(MediaOffsetX));
                OnPropertyChanged(nameof(MediaOffsetY));
            }
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public double? WidthScale { get; set; }

        [OnDeserialized]
        internal void OnDeserialized(StreamingContext context)
        {
            if (WidthScale is double ws)
            {
                double t = Math.Clamp((ws - 0.70) / (1.60 - 0.70), 0, 1);
                AspectRatio = MinAspect + (MaxAspect - MinAspect) * t;
                WidthScale = null;
            }
        }

        [JsonIgnore] public Visibility IsVideoVisible => MediaFileHelper.IsVideoFile(Path) ? Visibility.Visible : Visibility.Collapsed;
        [JsonIgnore] public Visibility IsImageVisible => IsVideoVisible == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        [JsonIgnore] public bool IsDragging { get => _isDragging; set { _isDragging = value; OnPropertyChanged(nameof(IsDragging)); } }
        [JsonIgnore] public bool IsDropTarget { get => _isDropTarget; set { _isDropTarget = value; OnPropertyChanged(nameof(IsDropTarget)); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

}

