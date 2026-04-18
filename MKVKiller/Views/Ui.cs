using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MKVKiller.Views;

internal static class Ui
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void ApplyDarkTitleBar(Window win)
    {
        try
        {
            var hwnd = new WindowInteropHelper(win).EnsureHandle();
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
        }
        catch { }
    }

    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public static TextBlock H2(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = Res("MutedBrush"),
        Margin = new Thickness(0, 14, 0, 8)
    };

    public static TextBlock Txt(string text, double size = 13, Brush? color = null, FontWeight? weight = null)
    {
        var tb = new TextBlock { Text = text, FontSize = size, Foreground = color ?? Res("TextBrush") };
        if (weight.HasValue) tb.FontWeight = weight.Value;
        return tb;
    }

    public static Border Panel(UIElement content, Thickness? pad = null)
    {
        var b = new Border
        {
            Background = Res("Panel2Brush"),
            BorderBrush = Res("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = pad ?? new Thickness(16),
            Margin = new Thickness(0, 0, 0, 14),
            Child = content
        };
        return b;
    }

    public static StackPanel HStack(params UIElement[] children)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) sp.Children.Add(c);
        return sp;
    }

    public static StackPanel VStack(params UIElement[] children)
    {
        var sp = new StackPanel();
        foreach (var c in children) sp.Children.Add(c);
        return sp;
    }

    public static Grid Grid2(params (string label, UIElement field)[] cells)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int row = 0, col = 0;
        foreach (var (label, field) in cells)
        {
            if (col == 0) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var sp = new StackPanel { Margin = new Thickness(col == 0 ? 0 : 6, 0, col == 1 ? 0 : 6, 8) };
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(field);
            Grid.SetRow(sp, row);
            Grid.SetColumn(sp, col);
            g.Children.Add(sp);
            col++;
            if (col == 2) { col = 0; row++; }
        }
        return g;
    }

    public static Button Btn(string text, RoutedEventHandler click, bool ghost = false, bool small = false, bool success = false, bool danger = false)
    {
        var b = new Button { Content = text };
        if (small)
        {
            b.Style = ghost ? (Style)Application.Current.FindResource("SmallGhostButton")
                             : (Style)Application.Current.FindResource("GhostButton");
            b.Padding = new Thickness(9, 5, 9, 5); b.FontSize = 11;
        }
        else if (ghost)
            b.Style = (Style)Application.Current.FindResource("GhostButton");
        else if (success)
            b.Style = (Style)Application.Current.FindResource("SuccessButton");
        if (danger)
            b.Background = Res("ErrBrush");
        b.Click += click;
        b.Margin = new Thickness(0, 0, 6, 0);
        return b;
    }

    public static CheckBox CheckBox(string text, bool? isChecked = false, Brush? accentColor = null)
    {
        var cb = new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            VerticalAlignment = VerticalAlignment.Center
        };
        return cb;
    }

    public static ComboBox Combo(IEnumerable<string> items, string? selected = null)
    {
        var c = new ComboBox();
        foreach (var i in items) c.Items.Add(i);
        if (selected != null) c.SelectedItem = selected;
        else c.SelectedIndex = 0;
        return c;
    }

    public static Border InfoBox(UIElement content, Brush? bg = null)
    {
        return new Border
        {
            Background = bg ?? Res("PanelBrush"),
            BorderBrush = Res("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = content
        };
    }
}
