using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MyButtonsWidget
{
    public partial class SettingsWindow : Window
    {
        private MainWindow? Main => Owner as MainWindow;

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += SettingsWindow_Loaded;
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;

            // Пробуем подсветить текущий цвет в пикере.
            if (Main.GlobalBorderBrush is SolidColorBrush current)
            {
                var match = ColorPicker.Items
                    .OfType<SolidColorBrush>()
                    .FirstOrDefault(b => b.Color == current.Color);

                if (match != null)
                    ColorPicker.SelectedItem = match;
            }
        }

        private void ColorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Main == null) return;
            if (ColorPicker.SelectedItem is not SolidColorBrush brush) return;

            // Создаём новый brush, чтобы не зависеть от instance из ресурсов ListBox.
            Main.GlobalBorderBrush = new SolidColorBrush(brush.Color);
        }

        private void PickBorderColor_Click(object sender, RoutedEventArgs e)
        {
            if (Main?.GlobalBorderBrush is not SolidColorBrush current) return;
            if (!TryPickColor(current.Color, out System.Windows.Media.Color color)) return;

            Main.GlobalBorderBrush = new SolidColorBrush(color);
        }

        private void PickPanelColor_Click(object sender, RoutedEventArgs e)
        {
            if (Main == null) return;
            if (!TryPickColor(Main.PanelBackgroundColor, out System.Windows.Media.Color color)) return;

            Main.PanelBackgroundColor = color;
        }

        private static bool TryPickColor(System.Windows.Media.Color initial, out System.Windows.Media.Color selected)
        {
            using var dialog = new Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                Color = Drawing.Color.FromArgb(initial.R, initial.G, initial.B)
            };

            if (dialog.ShowDialog() != Forms.DialogResult.OK)
            {
                selected = initial;
                return false;
            }

            selected = System.Windows.Media.Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Main?.SaveSettings();
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Main?.SaveSettings();
        }
    }
}

