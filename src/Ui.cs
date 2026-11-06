using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;

namespace Niuma;

static class Ui
{
    public static Brush Ink { get; private set; } = Color("#EEF5F4");
    public static Brush Muted { get; private set; } = Color("#ACC2C7");
    public static Brush Red { get; private set; } = Color("#FF7181");
    public static Brush Background { get; private set; } = Color("#13232D");
    public static Brush Panel { get; private set; } = Color("#213743");
    public static Brush Line { get; private set; } = Color("#36535E");
    public static Brush Accent { get; private set; } = Color("#86D8C2");
    private static readonly ControlTemplate ButtonTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="Surface" CornerRadius="9" Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                  Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Surface" Property="Opacity" Value="0.72"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="Surface" Property="Opacity" Value="0.42"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    public static Brush Color(string value) =>
        new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));

    public static void ApplyTheme(bool light)
    {
        var keys = new[] { "Ink", "Muted", "Red", "Background", "Panel", "Line", "Accent" };
        var colors = light
            ? new[] { "#203B43", "#526F78", "#C52642", "#F3F8F7", "#E3EFED", "#A4BEBB", "#247663" }
            : new[] { "#EEF5F4", "#ACC2C7", "#FF7181", "#13232D", "#213743", "#36535E", "#86D8C2" };
        var brushes = Array.ConvertAll(colors, Color);
        (Ink, Muted, Red, Background, Panel, Line, Accent) =
            (brushes[0], brushes[1], brushes[2], brushes[3], brushes[4], brushes[5], brushes[6]);
        for (int i = 0; i < brushes.Length; i++)
            Application.Current.Resources[keys[i]] = brushes[i];
        if (Application.Current.Resources.MergedDictionaries.Count == 0)
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Themes/Controls.xaml")
            });
    }
    public static TextBlock Text(string value = "", double size = 14, Brush? color = null)
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 3) };
        var key = ReferenceEquals(color, Accent) ? "Accent" : ReferenceEquals(color, Muted) ? "Muted" : "Ink";
        text.SetResourceReference(TextBlock.ForegroundProperty, key);
        return text;
    }

    public static TextBlock Money(string value = "", double size = 16)
    {
        var text = Text(value, size);
        text.FontWeight = FontWeights.SemiBold;
        text.SetResourceReference(TextBlock.ForegroundProperty, "Red");
        return text;
    }
    public static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(11, 6, 11, 6),
            Margin = new Thickness(0, 4, 7, 4), Cursor = System.Windows.Input.Cursors.Hand,
            Template = ButtonTemplate, BorderThickness = new Thickness(1),
            Foreground = primary ? Background : Ink, Background = primary ? Accent : Panel,
            BorderBrush = primary ? Accent : Line };
        button.SetResourceReference(Control.ForegroundProperty, primary ? "Background" : "Ink");
        button.SetResourceReference(Control.BackgroundProperty, primary ? "Accent" : "Panel");
        button.SetResourceReference(Control.BorderBrushProperty, primary ? "Accent" : "Line");
        button.Click += (_, _) => action();
        return button;
    }

    public static WrapPanel Row(params UIElement[] children)
    {
        var row = new WrapPanel();
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    public static StackPanel Stack(params UIElement[] children)
    {
        var stack = new StackPanel();
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }

    public static string Yuan(decimal amount) => "¥" + amount.ToString("N2");
    public static string Hours(decimal hours) => $"{hours:0.00} 小时";
    public static string Percent(double fraction) => $"{Math.Floor(fraction * 100)}%";
    public static string Clock(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }

    public static DateTimeOffset ParseStamp(string value)
    {
        if (!DateTime.TryParseExact(value.Trim(), ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("日期时间请用 YYYY-MM-DD HH:mm 格式。");
        if (TimeZoneInfo.Local.IsInvalidTime(date))
            throw new ArgumentException("该时间因本地时钟调整而不存在。");
        return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
    }

    public static string Stamp(DateTimeOffset date) => date.ToString("yyyy-MM-dd HH:mm:ss");
    public static decimal Amount(string input, string label)
    {
        if (!decimal.TryParse(input.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var value) || value < 0 || value > 1_000_000_000_000m || decimal.Round(value, 2) != value)
            throw new ArgumentException($"{label}须在 0 至 1 万亿元之间，最多输入两位小数。");
        return value;
    }

    public static void Error(Window owner, Exception error) =>
        MessageBox.Show(owner, error.Message, "桌面牛马", MessageBoxButton.OK, MessageBoxImage.Warning);
}

sealed class Meter : FrameworkElement
{
    private double fraction;
    public Meter()
    {
        Height = 7;
        Margin = new Thickness(0, 5, 0, 5);
    }

    public void Set(double value)
    {
        value = Math.Clamp(value, 0, 1);
        if (fraction == value) return;
        fraction = value;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        double radius = RenderSize.Height / 2;
        drawing.DrawRoundedRectangle(Ui.Line, null, new Rect(RenderSize), radius, radius);
        if (fraction > 0)
        {
            var fill = new Rect(0, 0, RenderSize.Width * fraction, RenderSize.Height);
            drawing.DrawRoundedRectangle(Ui.Accent, null, fill, radius, radius);
        }
    }
}

sealed class Editor : Window
{
    private readonly StackPanel fields = Ui.Stack();
    private readonly TextBlock status = Ui.Money();
    private readonly StackPanel footer = Ui.Stack();
    public Editor(Window owner, string title)
    {
        Owner = owner;
        Title = title;
        Width = 455;
        MaxHeight = SystemParameters.WorkArea.Height * .9;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;
        Foreground = Ui.Ink;
        SetResourceReference(BackgroundProperty, "Background");
        SetResourceReference(ForegroundProperty, "Ink");
        SourceInitialized += (_, _) =>
            WindowAppearance.Set(this, ((SolidColorBrush)Ui.Background).Color.R > 128);
        FontFamily = new FontFamily("Microsoft YaHei UI");
        var body = Ui.Stack(Ui.Text(title, 23), fields, status, footer);
        body.Margin = new Thickness(22);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public StackPanel Add(string label, FrameworkElement control)
    {
        control.Margin = new Thickness(0, 2, 0, 6);
        var section = Ui.Stack(Ui.Text(label, 13, Ui.Muted), control);
        fields.Children.Add(section);
        return section;
    }

    public TextBox Field(string label, string value = "", int limit = 100)
    {
        var box = new TextBox { Text = value, MaxLength = limit, Padding = new Thickness(6), FontSize = 14 };
        Add(label, box);
        return box;
    }

    public ComboBox Choice(string label, object values, int selected, string? display = null)
    {
        var choice = new ComboBox { ItemsSource = (System.Collections.IEnumerable)values,
            SelectedIndex = selected, DisplayMemberPath = display ?? "", Padding = new Thickness(6) };
        Add(label, choice);
        return choice;
    }

    public void Finish(Action save, Action? preview = null)
    {
        var buttons = Ui.Row(Ui.Button("保存", () => Run(() => { save(); DialogResult = true; }), true),
            Ui.Button("取消", () => DialogResult = false));
        if (preview != null) buttons.Children.Insert(0, Ui.Button("预览金额", () => Run(preview)));
        footer.Children.Add(buttons);
        ShowDialog();
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        { Status(error.Message); }
    }

    public void Status(string message) => status.Text = message;
}

static class WindowAppearance
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static void Set(Window window, bool light)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int dark = light ? 0 : 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
}
