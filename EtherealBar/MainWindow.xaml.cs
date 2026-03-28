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
        private const int MaxWorkspaces = 5;

        public enum PanelDockPosition
        {
            Bottom = 0,
            Top = 1,
            Left = 2,
            Right = 3
        }

        private ObservableCollection<ButtonConfig> _buttons = new ObservableCollection<ButtonConfig>();
        public ObservableCollection<ButtonConfig> Buttons
        {
            get => _buttons;
            private set
            {
                if (ReferenceEquals(_buttons, value)) return;
                _buttons = value;
                OnPropertyChanged(nameof(Buttons));
            }
        }

        public ObservableCollection<WorkspaceConfig> Workspaces { get; } = new ObservableCollection<WorkspaceConfig>();
        private WorkspaceConfig? _selectedWorkspace;
        public WorkspaceConfig? SelectedWorkspace
        {
            get => _selectedWorkspace;
            set
            {
                if (ReferenceEquals(_selectedWorkspace, value) || value == null) return;

                if (_isTileDragActive)
                    FinishTileDrag();

                _selectedWorkspace = value;
                Buttons = value.Buttons;
                OnPropertyChanged(nameof(SelectedWorkspace));

                if (_isPanelVisible)
                {
                    PauseAllMedia();
                    _targetOffset = 0;
                    _currentOffset = 0;
                    ScrollToOffset(0);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { PlayActiveWorkspaceMedia(); } catch { }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        private readonly string settingsFile = GetSettingsFilePath();

        private bool _isEditMode = false;
        private bool _isPanelVisible = false;
        private bool _isInternalShutdown = false;
        private Brush _globalBorderBrush = Brushes.Cyan;
        private Color _panelBackgroundColor = Color.FromRgb(5, 5, 5);
        private Brush _panelBackgroundBrush = Brushes.Transparent;
        private bool _isExiting = false;
        private PanelDockPosition _panelDock = PanelDockPosition.Bottom;

        private const double BaseTileHeight = 373;
        private const double MinTileHeight = 120;
        private const double TileVerticalPadding = 40;
        private const double MinWidgetHeightInEditMode = 320;
        private const double OverlayOutsideOffset = 40;
        private const double OverlayGap = 8;
        private const double VerticalDockOverlayWidth = 260;

        private double _widgetHeight = 400;
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
        private readonly Dictionary<WorkspaceConfig, ItemsControl> _workspaceMediaHosts = new Dictionary<WorkspaceConfig, ItemsControl>();
        private ItemsControl? _activeMediaHost;
        private SettingsWindow? _settingsWindow;
        private Forms.NotifyIcon? _notifyIcon;
        private readonly HashSet<WorkspaceConfig> _workspaceHooks = new HashSet<WorkspaceConfig>();
        private readonly Dictionary<WorkspaceConfig, int> _workspaceLastCounts = new Dictionary<WorkspaceConfig, int>();
        private bool _isLoadingSettings;

        public double MinWidgetHeight => IsEditMode ? MinWidgetHeightInEditMode : 200;

        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode == value) return;
                _isEditMode = value;
                OnPropertyChanged(nameof(IsEditMode));
                OnPropertyChanged(nameof(MinWidgetHeight));
                OnPropertyChanged(nameof(CanAddWorkspace));
                OnPropertyChanged(nameof(CanDeleteWorkspaces));

                if (_isEditMode && WidgetHeight < MinWidgetHeightInEditMode)
                {
                    WidgetHeight = MinWidgetHeightInEditMode;
                }
            }
        }

        public bool CanAddWorkspace => IsEditMode && Workspaces.Count < MaxWorkspaces;
        public bool CanDeleteWorkspaces => IsEditMode && Workspaces.Count > 1;
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

        public PanelDockPosition PanelDock
        {
            get => _panelDock;
            set
            {
                if (_panelDock == value) return;
                _panelDock = value;
                OnPropertyChanged(nameof(PanelDock));
                OnPropertyChanged(nameof(IsDockTop));
                OnPropertyChanged(nameof(IsDockBottom));
                OnPropertyChanged(nameof(IsDockLeft));
                OnPropertyChanged(nameof(IsDockRight));
                OnPropertyChanged(nameof(IsVerticalDock));
                OnPropertyChanged(nameof(IsHorizontalDock));
                OnPropertyChanged(nameof(WorkspaceItemsOrientation));
                OnPropertyChanged(nameof(WorkspaceTabsOrientation));
                OnPropertyChanged(nameof(PanelHeight));
                OnPropertyChanged(nameof(PanelWidth));
                OnPropertyChanged(nameof(TileMinor));
                OnPropertyChanged(nameof(AddTileButtonWidth));
                OnPropertyChanged(nameof(AddTileButtonHeight));
                UpdateLayoutMetrics();
            }
        }

        public bool IsDockTop
        {
            get => PanelDock == PanelDockPosition.Top;
            set { if (value) PanelDock = PanelDockPosition.Top; }
        }

        public bool IsDockBottom
        {
            get => PanelDock == PanelDockPosition.Bottom;
            set { if (value) PanelDock = PanelDockPosition.Bottom; }
        }

        public bool IsDockLeft
        {
            get => PanelDock == PanelDockPosition.Left;
            set { if (value) PanelDock = PanelDockPosition.Left; }
        }

        public bool IsDockRight
        {
            get => PanelDock == PanelDockPosition.Right;
            set { if (value) PanelDock = PanelDockPosition.Right; }
        }

        public bool IsVerticalDock => PanelDock == PanelDockPosition.Left || PanelDock == PanelDockPosition.Right;
        public bool IsHorizontalDock => !IsVerticalDock;

        // "Minor" tile dimension: cross-axis size for a tile.
        // Bottom/Top: we keep a constant tile height (cross-axis is vertical).
        // Left/Right: we keep a constant tile width (cross-axis is horizontal), but use the old TileWidth
        // so vertical cards don't become gigantic.
        public double TileMinor => IsVerticalDock ? TileWidth : TileHeight;

        public double PanelHeight => IsVerticalDock ? SystemParameters.WorkArea.Height : WidgetHeight;
        public double PanelWidth => IsVerticalDock ? WidgetHeight : SystemParameters.PrimaryScreenWidth;

        public System.Windows.Controls.Orientation WorkspaceItemsOrientation =>
            IsVerticalDock ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal;

        public System.Windows.Controls.Orientation WorkspaceTabsOrientation =>
            IsVerticalDock ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal;

        public double AddTileButtonWidth => IsVerticalDock ? TileMinor : 60;
        public double AddTileButtonHeight => IsVerticalDock ? 60 : TileMinor;

        public double WidgetHeight
        {
            get => _widgetHeight;
            set
            {
                double clamped = Clamp(value, MinWidgetHeight, MaxWidgetHeight);
                if (Math.Abs(_widgetHeight - clamped) < 0.1) return;
                _widgetHeight = clamped;
                OnPropertyChanged(nameof(WidgetHeight));
                OnPropertyChanged(nameof(TileWidth));
                OnPropertyChanged(nameof(TileHeight));
                OnPropertyChanged(nameof(TileMinor));
                OnPropertyChanged(nameof(PanelHeight));
                OnPropertyChanged(nameof(PanelWidth));
                OnPropertyChanged(nameof(AddTileButtonWidth));
                OnPropertyChanged(nameof(AddTileButtonHeight));
                UpdateLayoutMetrics();
            }
        }

        public double PanelBackgroundOpacity
        {
            get => _panelBackgroundOpacity;
            set
            {
                double clamped = Clamp(value, 0.0, 1.0);
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
                double availableHeight = WidgetHeight - TileVerticalPadding;
                return Math.Max(MinTileHeight, availableHeight);
            }
        }
        public double HiddenOffset => _hiddenOffset;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            Workspaces.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(CanAddWorkspace));
                OnPropertyChanged(nameof(CanDeleteWorkspaces));
            };
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Application.Current.Exit += OnAppExit;
            Application.Current.SessionEnding += App_SessionEnding;
            new WindowInteropHelper(this).EnsureHandle();
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

        private static string GetSettingsFilePath()
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EtherealBar");

            string targetPath = Path.Combine(appDataDir, "settings.json");
            string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

            try
            {
                if (!Directory.Exists(appDataDir))
                    Directory.CreateDirectory(appDataDir);

                if (!File.Exists(targetPath) && File.Exists(legacyPath))
                    File.Copy(legacyPath, targetPath, overwrite: false);
            }
            catch { }

            return targetPath;
        }

        private void UpdatePanelBackgroundBrush()
        {
            byte alpha = (byte)Math.Round(255 * PanelBackgroundOpacity);
            if (alpha == 0)
            {
                PanelBackgroundBrush = Brushes.Transparent;
                return;
            }

            PanelBackgroundBrush = new SolidColorBrush(Color.FromArgb(alpha, PanelBackgroundColor.R, PanelBackgroundColor.G, PanelBackgroundColor.B));
        }

        private void UpdateLayoutMetrics()
        {
            // Hidden translate should fully move the whole container out of view.
            _hiddenOffset = IsVerticalDock
                ? (PanelWidth + VerticalDockOverlayWidth + 50)
                : (WidgetHeight + 120);
            OnPropertyChanged(nameof(HiddenOffset));

            if (IsVerticalDock)
            {
                this.Height = SystemParameters.WorkArea.Height;
                this.Width = PanelWidth + VerticalDockOverlayWidth;
                this.Top = SystemParameters.WorkArea.Top;
                this.Left = PanelDock == PanelDockPosition.Left
                    ? SystemParameters.WorkArea.Left
                    : SystemParameters.WorkArea.Right - this.Width;

                FullPanelContainer.VerticalAlignment = VerticalAlignment.Stretch;
                FullPanelContainer.HorizontalAlignment =
                    PanelDock == PanelDockPosition.Left ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Right;

                if (MainPanel != null)
                {
                    MainPanel.VerticalAlignment = VerticalAlignment.Stretch;
                    MainPanel.HorizontalAlignment =
                        PanelDock == PanelDockPosition.Left ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Right;
                }

                if (MainScrollViewer != null)
                {
                    MainScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    MainScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                }
            }
            else
            {
                this.Width = SystemParameters.PrimaryScreenWidth;
                this.Left = 0;

                this.Height = WidgetHeight + 100;
                this.Top = PanelDock == PanelDockPosition.Top
                    ? SystemParameters.WorkArea.Top
                    : SystemParameters.WorkArea.Bottom - this.Height;

                FullPanelContainer.VerticalAlignment =
                    PanelDock == PanelDockPosition.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                FullPanelContainer.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;

                if (MainPanel != null)
                {
                    MainPanel.VerticalAlignment = VerticalAlignment.Bottom;
                    MainPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                }

                if (MainScrollViewer != null)
                {
                    MainScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
                    MainScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                }
            }

            // Tabs + top-right buttons sit "outside" the panel. Direction depends on dock.
            if (WorkspaceTabsScrollViewer != null)
            {
                if (IsVerticalDock)
                {
                    WorkspaceTabsScrollViewer.PanningMode = PanningMode.None;
                    WorkspaceTabsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    WorkspaceTabsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;

                    WorkspaceTabsScrollViewer.VerticalAlignment = VerticalAlignment.Top;
                    WorkspaceTabsScrollViewer.HorizontalAlignment =
                        PanelDock == PanelDockPosition.Left ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Right;

                    double xOffset = PanelWidth + OverlayGap + 12;
                    // Leave space for the top-right buttons so they don't overlap the first tab.
                    const double topOffset = 62;
                    if (PanelDock == PanelDockPosition.Left)
                        WorkspaceTabsScrollViewer.Margin = new Thickness(xOffset, topOffset, 12, 12);
                    else
                        WorkspaceTabsScrollViewer.Margin = new Thickness(12, topOffset, xOffset, 12);
                }
                else
                {
                    WorkspaceTabsScrollViewer.PanningMode = PanningMode.HorizontalOnly;
                    WorkspaceTabsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
                    WorkspaceTabsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

                    double offset = -(OverlayOutsideOffset + OverlayGap);
                    if (PanelDock == PanelDockPosition.Top)
                    {
                        WorkspaceTabsScrollViewer.VerticalAlignment = VerticalAlignment.Bottom;
                        WorkspaceTabsScrollViewer.Margin = new Thickness(12, 0, 160, offset);
                    }
                    else
                    {
                        WorkspaceTabsScrollViewer.VerticalAlignment = VerticalAlignment.Top;
                        WorkspaceTabsScrollViewer.Margin = new Thickness(12, offset, 160, 0);
                    }

                    WorkspaceTabsScrollViewer.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                }
            }

            if (TopRightButtonsPanel != null)
            {
                if (IsVerticalDock)
                {
                    TopRightButtonsPanel.VerticalAlignment = VerticalAlignment.Top;
                    TopRightButtonsPanel.HorizontalAlignment =
                        PanelDock == PanelDockPosition.Left ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Right;
                    TopRightButtonsPanel.RenderTransform = null;

                    double xOffset = PanelWidth + OverlayGap + 12;
                    if (PanelDock == PanelDockPosition.Left)
                        TopRightButtonsPanel.Margin = new Thickness(xOffset, 12, 12, 0);
                    else
                        TopRightButtonsPanel.Margin = new Thickness(12, 12, xOffset, 0);
                }
                else
                {
                    TopRightButtonsPanel.RenderTransform = null;

                    double offset = -(OverlayOutsideOffset + OverlayGap);
                    if (PanelDock == PanelDockPosition.Top)
                    {
                        TopRightButtonsPanel.VerticalAlignment = VerticalAlignment.Bottom;
                        TopRightButtonsPanel.Margin = new Thickness(12, 0, 12, offset);
                    }
                    else
                    {
                        TopRightButtonsPanel.VerticalAlignment = VerticalAlignment.Top;
                        TopRightButtonsPanel.Margin = new Thickness(12, offset, 12, 0);
                    }

                    TopRightButtonsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                }
            }

            if (!_isPanelVisible)
            {
                var hidden = GetHiddenTranslate();
                PanelTransform.X = hidden.x;
                PanelTransform.Y = hidden.y;
            }
        }

        private (double x, double y) GetHiddenTranslate()
        {
            if (IsVerticalDock)
            {
                return PanelDock == PanelDockPosition.Left ? (-HiddenOffset, 0) : (HiddenOffset, 0);
            }

            return PanelDock == PanelDockPosition.Top ? (0, -HiddenOffset) : (0, HiddenOffset);
        }

        private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            _isInternalShutdown = true;

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
            var hidden = GetHiddenTranslate();
            bool slideX = IsVerticalDock;
            double target = _isPanelVisible ? 0 : (slideX ? hidden.x : hidden.y);
            DoubleAnimation anim = new DoubleAnimation(target, TimeSpan.FromSeconds(0.4)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };

            if (_isPanelVisible)
            {
                this.Show();
                this.Opacity = 0;
                this.Activate();
                FocusManager.SetFocusedElement(this, MainScrollViewer);
                this.Topmost = true;

                ManageAllMedia(true);
                CompositionTarget.Rendering += OnRenderFrame;
                if (slideX) PanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
                else PanelTransform.BeginAnimation(TranslateTransform.YProperty, anim);

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
                if (slideX) PanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
                else PanelTransform.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }

        private void ManageAllMedia(bool play)
        {
            if (play) PlayActiveWorkspaceMedia();
            else PauseAllMedia();
        }

        private void PauseAllMedia()
        {
            foreach (var host in _workspaceMediaHosts.Values)
            {
                PauseMediaInHost(host);
            }
        }

        private void PlayActiveWorkspaceMedia()
        {
            if (SelectedWorkspace != null && _workspaceMediaHosts.TryGetValue(SelectedWorkspace, out var host))
            {
                _activeMediaHost = host;
            }

            if (_activeMediaHost != null)
            {
                PlayMediaInHost(_activeMediaHost);
            }
        }

        private void PauseMediaInHost(ItemsControl host)
        {
            for (int i = 0; i < host.Items.Count; i++)
            {
                var container = host.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                var media = FindVisualChild<MediaElement>(container);
                if (media == null) continue;
                try { media.Pause(); } catch { }
            }
        }

        private void PlayMediaInHost(ItemsControl host)
        {
            for (int i = 0; i < host.Items.Count; i++)
            {
                var container = host.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                var media = FindVisualChild<MediaElement>(container);
                if (media == null) continue;
                try
                {
                    if (media.Visibility == Visibility.Visible)
                        media.Play();
                }
                catch { }
            }
        }

        private void WorkspaceMediaHost_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ItemsControl host) return;
            if (host.DataContext is not WorkspaceConfig ws) return;

            _workspaceMediaHosts[ws] = host;
            if (SelectedWorkspace != null && ReferenceEquals(ws, SelectedWorkspace))
            {
                _activeMediaHost = host;
                if (_isPanelVisible)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { PlayActiveWorkspaceMedia(); } catch { }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        private void WorkspaceMediaHost_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ItemsControl host) return;
            if (host.DataContext is not WorkspaceConfig ws) return;

            if (_workspaceMediaHosts.TryGetValue(ws, out var existing) && ReferenceEquals(existing, host))
                _workspaceMediaHosts.Remove(ws);

            if (ReferenceEquals(_activeMediaHost, host))
                _activeMediaHost = null;
        }

        private void ForceExit()
        {
            if (_isExiting) return;
            _isExiting = true;

            try
            {
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

                IntPtr h = new WindowInteropHelper(this).Handle;
                UnregisterHotKey(h, HOTKEY_ID);

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
                ScrollToOffset(_currentOffset);
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double extent = IsVerticalDock ? MainScrollViewer.ScrollableHeight : MainScrollViewer.ScrollableWidth;
            _targetOffset = Math.Max(0, Math.Min(extent, _targetOffset - (e.Delta / 120.0) * 250));
            e.Handled = true;
        }

        private void ScrollToOffset(double offset)
        {
            if (IsVerticalDock)
                MainScrollViewer.ScrollToVerticalOffset(offset);
            else
                MainScrollViewer.ScrollToHorizontalOffset(offset);
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
            _isLoadingSettings = true;
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
                        PanelBackgroundOpacity = s.PanelBackgroundOpacity;
                        WidgetHeight = s.WidgetHeight;
                        string dock = (s.PanelDock ?? string.Empty).Trim();
                        if (string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase))
                            PanelDock = PanelDockPosition.Top;
                        else if (string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase))
                            PanelDock = PanelDockPosition.Left;
                        else if (string.Equals(dock, "Right", StringComparison.OrdinalIgnoreCase))
                            PanelDock = PanelDockPosition.Right;
                        else
                            PanelDock = PanelDockPosition.Bottom;

                        bool collapsedToSingle = false;
                        Workspaces.Clear();
                        if (s.Workspaces != null && s.Workspaces.Count > 0)
                        {
                            // Миграция: если в сохранённых настройках лежит "дефолтный набор" вкладок
                            // (Игры/Программы/Документы/Файлы) и кроме "Программы" они пустые,
                            // то сворачиваем всё в одну вкладку "Программы".
                            if (ShouldCollapseDefaultWorkspacesToSinglePrograms(s.Workspaces))
                            {
                                var programs = s.Workspaces.FirstOrDefault(w =>
                                    string.Equals(w.Name, "Программы", StringComparison.OrdinalIgnoreCase));
                                var buttons = programs?.Buttons != null
                                    ? new ObservableCollection<ButtonConfig>(programs.Buttons)
                                    : new ObservableCollection<ButtonConfig>();
                                Workspaces.Add(new WorkspaceConfig("Программы", buttons));
                                collapsedToSingle = true;
                            }
                            else
                            {
                                foreach (var ws in s.Workspaces.Take(MaxWorkspaces))
                                {
                                    var buttons = ws.Buttons != null
                                        ? new ObservableCollection<ButtonConfig>(ws.Buttons)
                                        : new ObservableCollection<ButtonConfig>();
                                    Workspaces.Add(new WorkspaceConfig(ws.Name ?? "Программы", buttons));
                                }
                            }
                        }
                        else
                        {
                            var legacyButtons = s.Buttons ?? new List<ButtonConfig>();
                            // По умолчанию создаём одну вкладку "Программы" и переносим туда старые карточки.
                            Workspaces.Add(new WorkspaceConfig("Программы", new ObservableCollection<ButtonConfig>(legacyButtons)));
                        }

                        if (Workspaces.Count == 0)
                            Workspaces.Add(new WorkspaceConfig("Программы", new ObservableCollection<ButtonConfig>()));

                        WorkspaceConfig? initialWorkspace = null;
                        if (!string.IsNullOrWhiteSpace(s.SelectedWorkspaceName))
                        {
                            initialWorkspace = Workspaces.FirstOrDefault(w =>
                                string.Equals(w.Name, s.SelectedWorkspaceName, StringComparison.OrdinalIgnoreCase));
                        }

                        initialWorkspace ??=
                            Workspaces.FirstOrDefault(w => string.Equals(w.Name, "Программы", StringComparison.OrdinalIgnoreCase))
                            ?? Workspaces[0];

                        SelectedWorkspace = initialWorkspace;

                        if (collapsedToSingle)
                        {
                            // Persist the migration so next launches don't show old default tabs.
                            try { SaveSettings(); } catch { }
                        }
                    }
                }
                catch { }

            if (Workspaces.Count == 0)
            {
                Workspaces.Add(new WorkspaceConfig("Программы", Buttons));
            }

            if (SelectedWorkspace == null)
            {
                SelectedWorkspace =
                    Workspaces.FirstOrDefault(w => string.Equals(w.Name, "Программы", StringComparison.OrdinalIgnoreCase))
                    ?? Workspaces[0];
            }

            // Hook workspace button lists for auto-cleanup and rename focus.
            foreach (var ws in Workspaces)
            {
                HookWorkspace(ws);
            }

            _isLoadingSettings = false;

            if (Buttons.Count == 0) Buttons.Add(new ButtonConfig { Title = "Desktop" });

            foreach (var button in Workspaces.SelectMany(w => w.Buttons))
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
                    PanelBackgroundOpacity = PanelBackgroundOpacity,
                    PanelDock = PanelDock switch
                    {
                        PanelDockPosition.Top => "Top",
                        PanelDockPosition.Left => "Left",
                        PanelDockPosition.Right => "Right",
                        _ => "Bottom"
                    },
                    SelectedWorkspaceName = SelectedWorkspace?.Name,
                    Workspaces = Workspaces
                        .Select(w => new WorkspaceSettings
                        {
                            Name = w.Name,
                            Buttons = w.Buttons.ToList()
                        })
                        .ToList(),
                    // Совместимость со старым форматом: сохраняем активную вкладку в поле Buttons.
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

        private void WorkspaceTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is WorkspaceConfig ws)
            {
                SelectedWorkspace = ws;
            }
        }

        private void WorkspaceTab_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border) return;

            // Don't switch workspace when clicking on action buttons inside the tab.
            if (FindAncestor<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject, border) != null)
                return;

            if (border.Tag is WorkspaceConfig ws)
            {
                SelectedWorkspace = ws;
            }
        }

        private void Tile_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditMode && sender is Button b && b.DataContext is ButtonConfig c)
            {
                try { Process.Start(new ProcessStartInfo(c.AppPath) { UseShellExecute = true }); TogglePanel(); } catch { }
            }
        }

        private void ToggleEditMode_Click(object sender, RoutedEventArgs e)
        {
            IsEditMode = !IsEditMode;
            if (!IsEditMode)
            {
                PruneEmptyWorkspaces();
                SaveSettings();
            }
        }
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

        private void AddWorkspace_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditMode) return;
            if (Workspaces.Count >= MaxWorkspaces) return;

            string name = MakeUniqueWorkspaceName($"Вкладка {Workspaces.Count + 1}", null);
            var ws = new WorkspaceConfig(name, new ObservableCollection<ButtonConfig>());
            Workspaces.Add(ws);
            HookWorkspace(ws);
            SelectedWorkspace = ws;
            SaveSettings();

            Dispatcher.BeginInvoke(new Action(() => FocusWorkspaceNameEditor(ws)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void WorkspaceClose_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!IsEditMode) return;
            if (Workspaces.Count <= 1) return;
            if (sender is not Button b || b.Tag is not WorkspaceConfig ws) return;

            int index = Workspaces.IndexOf(ws);
            if (index < 0) return;

            bool wasSelected = ReferenceEquals(SelectedWorkspace, ws);
            Workspaces.Remove(ws);

            if (wasSelected && Workspaces.Count > 0)
            {
                int nextIndex = Math.Clamp(index, 0, Workspaces.Count - 1);
                SelectedWorkspace = Workspaces[nextIndex];
            }

            SaveSettings();
        }

        private void WorkspaceRenameButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!IsEditMode) return;
            if (sender is not Button b || b.Tag is not WorkspaceConfig ws) return;

            ws.IsRenaming = true;
            Dispatcher.BeginInvoke(new Action(() => FocusWorkspaceNameEditor(ws)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void WorkspaceName_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb)
            {
                tb.Tag = tb.Text;
            }
        }

        private void WorkspaceName_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox tb) return;

            if (e.Key == System.Windows.Input.Key.Enter)
            {
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (tb.Tag is string original)
                {
                    tb.Text = original;
                }
                if (tb.DataContext is WorkspaceConfig ws)
                {
                    ws.IsRenaming = false;
                }
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        private void WorkspaceName_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox tb) return;
            if (tb.DataContext is not WorkspaceConfig ws) return;

            string proposed = (ws.Name ?? string.Empty).Trim();
            if (proposed.Length == 0)
                proposed = "Программы";

            string unique = MakeUniqueWorkspaceName(proposed, ws);
            if (!string.Equals(ws.Name, unique, StringComparison.Ordinal))
                ws.Name = unique;

            ws.IsRenaming = false;
            SaveSettings();
        }

        private string MakeUniqueWorkspaceName(string proposed, WorkspaceConfig? self)
        {
            string baseName = (proposed ?? string.Empty).Trim();
            if (baseName.Length == 0) baseName = "Программы";

            bool Conflicts(string n) =>
                Workspaces.Any(w =>
                    !ReferenceEquals(w, self) &&
                    string.Equals(w.Name, n, StringComparison.OrdinalIgnoreCase));

            if (!Conflicts(baseName))
                return baseName;

            for (int i = 2; i <= 99; i++)
            {
                string candidate = $"{baseName} ({i})";
                if (!Conflicts(candidate))
                    return candidate;
            }

            // Fallback: timestamp suffix
            return $"{baseName} ({DateTime.Now:HHmmss})";
        }

        private void FocusWorkspaceNameEditor(WorkspaceConfig ws)
        {
            if (!IsEditMode) return;

            var container = WorkspacesTabsItems?.ItemContainerGenerator.ContainerFromItem(ws) as FrameworkElement;
            if (container == null) return;

            var tb = FindVisualChild<System.Windows.Controls.TextBox>(container);
            if (tb == null) return;

            tb.Focus();
            tb.SelectAll();
        }

        private void HookWorkspace(WorkspaceConfig ws)
        {
            if (_workspaceHooks.Contains(ws))
                return;

            _workspaceHooks.Add(ws);
            _workspaceLastCounts[ws] = ws.Buttons.Count;
            ws.Buttons.CollectionChanged += (_, __) => OnWorkspaceButtonsChanged(ws);
        }

        private void OnWorkspaceButtonsChanged(WorkspaceConfig ws)
        {
            if (_isLoadingSettings) return;

            int last = _workspaceLastCounts.TryGetValue(ws, out var v) ? v : ws.Buttons.Count;
            int now = ws.Buttons.Count;
            _workspaceLastCounts[ws] = now;

            // Auto-remove workspace when user deleted all cards from it (but never remove the last workspace).
            if (last > 0 && now == 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    PruneEmptyWorkspaces();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void PruneEmptyWorkspaces()
        {
            if (Workspaces.Count <= 1) return;

            var empty = Workspaces.Where(w => w.Buttons.Count == 0).ToList();
            if (empty.Count == 0) return;

            foreach (var ws in empty)
            {
                if (Workspaces.Count <= 1) break;
                if (!Workspaces.Contains(ws)) continue;

                bool wasSelected = ReferenceEquals(SelectedWorkspace, ws);
                int idx = Workspaces.IndexOf(ws);
                Workspaces.Remove(ws);

                if (wasSelected && Workspaces.Count > 0)
                {
                    int next = Math.Clamp(idx, 0, Workspaces.Count - 1);
                    SelectedWorkspace = Workspaces[next];
                }
            }

            SaveSettings();
        }

        private static bool ShouldCollapseDefaultWorkspacesToSinglePrograms(List<WorkspaceSettings> workspaces)
        {
            if (workspaces.Count < 2)
                return false;

            bool hasOnlyDefaultNames = workspaces.All(ws =>
            {
                string n = (ws.Name ?? string.Empty).Trim();
                return string.Equals(n, "Игры", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(n, "Программы", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(n, "Документы", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(n, "Файлы", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(n, "Основное", StringComparison.OrdinalIgnoreCase);
            });

            if (!hasOnlyDefaultNames)
                return false;

            bool othersEmpty = workspaces.All(ws =>
            {
                string n = (ws.Name ?? string.Empty).Trim();
                if (string.Equals(n, "Программы", StringComparison.OrdinalIgnoreCase))
                    return true;

                return IsWorkspaceEffectivelyEmpty(ws);
            });

            return othersEmpty;
        }

        private static bool IsWorkspaceEffectivelyEmpty(WorkspaceSettings ws)
        {
            if (ws.Buttons == null || ws.Buttons.Count == 0)
                return true;

            // Some older versions created "заглушки" (пустые карточки).
            // Если во вкладке только такие элементы, считаем её пустой для миграции.
            return ws.Buttons.All(IsPlaceholderButton);
        }

        private static bool IsPlaceholderButton(ButtonConfig b)
        {
            if (b == null) return true;

            bool hasMedia = !string.IsNullOrWhiteSpace(b.Path);
            if (hasMedia) return false;

            string app = (b.AppPath ?? string.Empty).Trim();
            bool isDefaultApp = app.Length == 0
                                || string.Equals(app, "explorer.exe", StringComparison.OrdinalIgnoreCase);

            string title = (b.Title ?? string.Empty).Trim();
            bool isDefaultTitle = title.Length == 0
                                  || string.Equals(title, "New", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(title, "Desktop", StringComparison.OrdinalIgnoreCase);

            return isDefaultApp && isDefaultTitle;
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

                var host = _activeMediaHost;
                if (host == null)
                {
                    continue;
                }

                var presenter = host.ItemContainerGenerator.ContainerFromItem(candidate) as ContentPresenter;
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
        public string PanelDock { get; set; } = "Bottom";
        public string? SelectedWorkspaceName { get; set; }
        public List<WorkspaceSettings>? Workspaces { get; set; }
        public List<ButtonConfig> Buttons { get; set; } = new List<ButtonConfig>();
    }

    public class WorkspaceSettings
    {
        public string? Name { get; set; }
        public List<ButtonConfig>? Buttons { get; set; }
    }

    public class WorkspaceConfig : INotifyPropertyChanged
    {
        private string _name;
        private bool _isRenaming;

        public WorkspaceConfig(string name, ObservableCollection<ButtonConfig> buttons)
        {
            _name = name;
            Buttons = buttons;
        }

        public string Name
        {
            get => _name;
            set
            {
                string v = value ?? string.Empty;
                if (string.Equals(_name, v, StringComparison.Ordinal)) return;
                _name = v;
                OnPropertyChanged(nameof(Name));
            }
        }

        [JsonIgnore]
        public bool IsRenaming
        {
            get => _isRenaming;
            set
            {
                if (_isRenaming == value) return;
                _isRenaming = value;
                OnPropertyChanged(nameof(IsRenaming));
            }
        }

        public ObservableCollection<ButtonConfig> Buttons { get; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
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
        public string Path
        {
            get => _p;
            set
            {
                _p = value;
                OnPropertyChanged(nameof(Path));
                OnPropertyChanged(nameof(IsVideoVisible));
                OnPropertyChanged(nameof(IsGifVisible));
                OnPropertyChanged(nameof(IsStaticImageVisible));
            }
        }
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
        [JsonIgnore] public Visibility IsGifVisible => MediaFileHelper.IsGifFile(Path) ? Visibility.Visible : Visibility.Collapsed;
        [JsonIgnore] public Visibility IsStaticImageVisible =>
            (!MediaFileHelper.IsVideoFile(Path) && !MediaFileHelper.IsGifFile(Path)) ? Visibility.Visible : Visibility.Collapsed;
        [JsonIgnore] public bool IsDragging { get => _isDragging; set { _isDragging = value; OnPropertyChanged(nameof(IsDragging)); } }
        [JsonIgnore] public bool IsDropTarget { get => _isDropTarget; set { _isDropTarget = value; OnPropertyChanged(nameof(IsDropTarget)); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

}

