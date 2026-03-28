using System;
using System.Windows;

namespace EtherealBar
{
    internal sealed class TextPromptDialog : Window
    {
        private readonly System.Windows.Controls.TextBox _textBox;
        private string? _result;

        private TextPromptDialog(string title, string caption, string initialValue)
        {
            Title = title;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 18, 18));
            Foreground = System.Windows.Media.Brushes.White;
            WindowStyle = WindowStyle.ToolWindow;
            ShowInTaskbar = false;

            var root = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(18),
                MinWidth = 420
            };
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var captionBlock = new System.Windows.Controls.TextBlock
            {
                Text = caption,
                Opacity = 0.85,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 14
            };
            System.Windows.Controls.Grid.SetRow(captionBlock, 0);
            root.Children.Add(captionBlock);

            _textBox = new System.Windows.Controls.TextBox
            {
                Text = initialValue,
                FontSize = 14,
                Padding = new Thickness(10, 6, 10, 6),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 14)
            };
            _textBox.SelectAll();
            _textBox.Focus();
            System.Windows.Controls.Grid.SetRow(_textBox, 1);
            root.Children.Add(_textBox);

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var ok = new System.Windows.Controls.Button
            {
                Content = "ОК",
                MinWidth = 90,
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(14, 6, 14, 6)
            };
            ok.Click += (_, _) => Accept();

            var cancel = new System.Windows.Controls.Button
            {
                Content = "Отмена",
                MinWidth = 90,
                Padding = new Thickness(14, 6, 14, 6)
            };
            cancel.Click += (_, _) => { DialogResult = false; Close(); };

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            System.Windows.Controls.Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Content = root;

            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) Accept();
                if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; Close(); }
            };
        }

        private void Accept()
        {
            _result = _textBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(_result))
                return;

            DialogResult = true;
            Close();
        }

        public static string? Show(Window owner, string title, string caption, string initialValue)
        {
            var dlg = new TextPromptDialog(title, caption, initialValue)
            {
                Owner = owner
            };

            bool? ok = dlg.ShowDialog();
            return ok == true ? dlg._result : null;
        }
    }
}
