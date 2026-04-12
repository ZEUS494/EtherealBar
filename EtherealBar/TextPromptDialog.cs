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
            Background = System.Windows.Media.Brushes.Transparent;
            AllowsTransparency = true;
            WindowStyle = WindowStyle.None;
            ShowInTaskbar = false;

            var windowBorder = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 10, 10)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(24)
            };

            var root = new System.Windows.Controls.Grid
            {
                MinWidth = 420
            };
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var captionBlock = new System.Windows.Controls.TextBlock
            {
                Text = caption,
                Foreground = System.Windows.Media.Brushes.White,
                Opacity = 0.6,
                Margin = new Thickness(0, 0, 0, 16),
                FontSize = 14
            };
            System.Windows.Controls.Grid.SetRow(captionBlock, 0);
            root.Children.Add(captionBlock);

            _textBox = new System.Windows.Controls.TextBox
            {
                Text = initialValue,
                FontSize = 15,
                Padding = new Thickness(12, 8, 12, 8),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CaretBrush = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 24)
            };

            // Modern TextBox Template (simplified)
            var textBoxTemplate = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.TextBox));
            var borderFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            borderFactory.Name = "border";
            borderFactory.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.TextBox.BackgroundProperty));
            borderFactory.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(System.Windows.Controls.TextBox.BorderBrushProperty));
            borderFactory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new TemplateBindingExtension(System.Windows.Controls.TextBox.BorderThicknessProperty));
            borderFactory.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(10));
            
            var contentHostFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.ScrollViewer));
            contentHostFactory.Name = "PART_ContentHost";
            contentHostFactory.SetValue(System.Windows.Controls.ScrollViewer.MarginProperty, new Thickness(0));
            contentHostFactory.SetValue(System.Windows.Controls.ScrollViewer.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentHostFactory);
            textBoxTemplate.VisualTree = borderFactory;
            _textBox.Template = textBoxTemplate;

            _textBox.SelectAll();
            _textBox.Focus();
            System.Windows.Controls.Grid.SetRow(_textBox, 1);
            root.Children.Add(_textBox);

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var ok = CreateStyledButton("ОК", true);
            ok.Margin = new Thickness(0, 0, 12, 0);
            ok.Click += (_, _) => Accept();

            var cancel = CreateStyledButton("Отмена", false);
            cancel.Click += (_, _) => { DialogResult = false; Close(); };

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            System.Windows.Controls.Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            windowBorder.Child = root;
            Content = windowBorder;

            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) Accept();
                if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; Close(); }
            };
        }

        private System.Windows.Controls.Button CreateStyledButton(string text, bool isPrimary)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = text,
                MinWidth = 100,
                Height = 38,
                Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 14,
                FontWeight = isPrimary ? FontWeights.Bold : FontWeights.Normal
            };

            var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
            var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.Name = "border";
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(isPrimary ? (byte)40 : (byte)20, 255, 255, 255)));
            border.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)));
            border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));

            var content = new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            content.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            content.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;

            var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 255, 255, 255)), "border"));
            template.Triggers.Add(trigger);

            btn.Template = template;
            return btn;
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
