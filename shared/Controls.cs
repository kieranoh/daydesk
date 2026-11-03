using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

static class Theme
{
    public static SolidColorBrush Brush(string color)
    { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    public static readonly Brush Ink = Brush("#F4F8FC"), Muted = Brush("#D5E3E8"), Green = Brush("#2ECC87"), Line = Brush("#35FFFFFF");

    public static TextBlock Text(string value, double size = 13, bool bold = false)
    {
        return new TextBlock { Text = value, FontSize = size, Foreground = Ink, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = .65 } };
    }

    public static void StyleButton(ButtonBase button, string name)
    {
        button.Padding = new Thickness(10, 6, 10, 6); button.Margin = new Thickness(2);
        button.Background = Brush("#18FFFFFF"); button.Foreground = Ink; button.BorderBrush = Brushes.Transparent;
        button.BorderThickness = new Thickness(1); button.Cursor = Cursors.Hand; button.FontSize = 13;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.PaddingProperty })
            border.SetBinding(property, new Binding(property.Name) { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(presenter);
        var template = new ControlTemplate(button.GetType()) { VisualTree = border };
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, Green)); template.Triggers.Add(focus);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BorderBrushProperty, Line)); template.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45)); template.Triggers.Add(disabled);
        button.Template = template; AutomationProperties.SetName(button, name);
    }

    public static Button Button(string label, Action action, string name = null)
    {
        var button = new Button { Content = label };
        StyleButton(button, name ?? label); button.Click += delegate { action(); }; return button;
    }

    public static ToggleButton Toggle(string label, bool initial, Action<bool> action, Brush accent = null)
    {
        var toggle = new ToggleButton { IsChecked = initial };
        StyleButton(toggle, label);
        var text = new TextBlock { MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis };
        toggle.Content = text; toggle.ToolTip = label;
        Action update = delegate {
            bool on = toggle.IsChecked == true;
            text.Text = label + (on ? " · 켬" : " · 끔");
            toggle.Background = on ? accent ?? Green : Brush("#14000000");
            toggle.Foreground = on ? Brush("#13272C") : Muted;
        };
        update();
        toggle.Click += delegate { update(); action(toggle.IsChecked == true); };
        return toggle;
    }
}
