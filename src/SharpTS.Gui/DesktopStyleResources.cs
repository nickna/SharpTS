using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace SharpTS.Gui;

internal sealed partial class DesktopStyleResources
{
    private readonly Dictionary<string, JsonElement> _resources;
    private readonly StyleModel[] _styles;

    private DesktopStyleResources(Dictionary<string, JsonElement> resources, StyleModel[] styles)
    {
        _resources = resources;
        _styles = styles;
    }

    public static DesktopStyleResources Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ContractModel model = JsonSerializer.Deserialize(
            json,
            StyleJsonContext.Default.ContractModel)
            ?? throw new ArgumentException("The desktop style/resource contract is invalid.", nameof(json));
        var resources = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string key, JsonElement value) in model.Resources ?? [])
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (!resources.TryAdd(key, value.Clone()))
                throw new ArgumentException($"Duplicate desktop resource '{key}'.", nameof(json));
            _ = Primitive(value, $"resource '{key}'");
        }
        foreach (StyleModel style in model.Styles ?? [])
            ValidateStyle(style, resources);
        return new DesktopStyleResources(resources, model.Styles ?? []);
    }

    public void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        foreach ((string key, JsonElement value) in _resources)
            window.Resources[key] = Primitive(value, $"resource '{key}'");
        foreach (StyleModel model in _styles)
        {
            Style style = CreateStyle(model.Selector!);
            foreach ((string property, JsonElement value) in model.Setters ?? [])
                style.Setters.Add(CreateSetter(property, Resolve(value), model.Selector!.Control));
            window.Styles.Add(style);
        }
    }

    private object Resolve(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("resource", out JsonElement resourceName))
        {
            string key = resourceName.GetString()
                ?? throw new ArgumentException("A style resource reference requires a string key.");
            if (!_resources.TryGetValue(key, out JsonElement resource))
                throw new ArgumentException($"Desktop resource '{key}' was not declared.");
            return Primitive(resource, $"resource '{key}'");
        }
        return Primitive(value, "style value");
    }

    private static void ValidateStyle(StyleModel model, IReadOnlyDictionary<string, JsonElement> resources)
    {
        if (model.Selector is null)
            throw new ArgumentException("Each desktop style requires a selector.");
        _ = CreateStyle(model.Selector);
        if (model.Setters is null || model.Setters.Count == 0)
            throw new ArgumentException("Each desktop style requires at least one setter.");
        foreach ((string property, JsonElement value) in model.Setters)
        {
            object resolved;
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("resource", out JsonElement reference))
            {
                string key = reference.GetString() ?? string.Empty;
                if (!resources.TryGetValue(key, out JsonElement resource))
                    throw new ArgumentException($"Desktop resource '{key}' was not declared.");
                resolved = Primitive(resource, $"resource '{key}'");
            }
            else
            {
                resolved = Primitive(value, "style value");
            }
            _ = CreateSetter(property, resolved, model.Selector.Control);
        }
    }

    private static Style CreateStyle(SelectorModel selector)
    {
        string[] classes = selector.Classes ?? [];
        if (classes.Any(string.IsNullOrWhiteSpace) || classes.Any(value => value[0] == ':') ||
            classes.Distinct(StringComparer.Ordinal).Count() != classes.Length)
            throw new ArgumentException("Style selector classes must be unique, non-empty user class names.");
        return selector.Control switch
        {
            "Control" => TypedStyle<Control>(classes),
            "Window" => TypedStyle<Window>(classes),
            "StackPanel" or "ToolBar" => TypedStyle<StackPanel>(classes),
            "WrapPanel" => TypedStyle<WrapPanel>(classes),
            "DockPanel" => TypedStyle<DockPanel>(classes),
            "Grid" => TypedStyle<Grid>(classes),
            "Border" or "StatusBar" => TypedStyle<Border>(classes),
            "ScrollViewer" => TypedStyle<ScrollViewer>(classes),
            "TextBlock" => TypedStyle<TextBlock>(classes),
            "Button" => TypedStyle<Button>(classes),
            "TextBox" or "PasswordBox" => TypedStyle<TextBox>(classes),
            "CheckBox" => TypedStyle<CheckBox>(classes),
            "RadioButton" => TypedStyle<RadioButton>(classes),
            "ToggleSwitch" => TypedStyle<ToggleSwitch>(classes),
            "ComboBox" => TypedStyle<ComboBox>(classes),
            "ListBox" => TypedStyle<ListBox>(classes),
            "NumericUpDown" => TypedStyle<NumericUpDown>(classes),
            "DatePicker" => TypedStyle<DatePicker>(classes),
            "TimePicker" => TypedStyle<TimePicker>(classes),
            "Slider" => TypedStyle<Slider>(classes),
            "ProgressBar" => TypedStyle<ProgressBar>(classes),
            "Separator" => TypedStyle<Separator>(classes),
            "Image" => TypedStyle<Image>(classes),
            "TabControl" => TypedStyle<TabControl>(classes),
            "TabItem" => TypedStyle<TabItem>(classes),
            "Menu" => TypedStyle<Menu>(classes),
            "MenuItem" => TypedStyle<MenuItem>(classes),
            _ => throw new ArgumentException($"Unsupported desktop style selector control '{selector.Control}'."),
        };
    }

    private static Style TypedStyle<T>(IEnumerable<string> classes) where T : StyledElement =>
        new(selector => classes.Aggregate(selector.OfType<T>(), (current, styleClass) => current.Class(styleClass)));

    private static Setter CreateSetter(string property, object value, string control) => property switch
    {
        "width" => new Setter(Control.WidthProperty, Number(value, property)),
        "height" => new Setter(Control.HeightProperty, Number(value, property)),
        "minWidth" => new Setter(Control.MinWidthProperty, Number(value, property)),
        "minHeight" => new Setter(Control.MinHeightProperty, Number(value, property)),
        "maxWidth" => new Setter(Control.MaxWidthProperty, Number(value, property)),
        "maxHeight" => new Setter(Control.MaxHeightProperty, Number(value, property)),
        "opacity" => new Setter(Visual.OpacityProperty, Number(value, property)),
        "isVisible" => new Setter(Visual.IsVisibleProperty, Boolean(value, property)),
        "isEnabled" => new Setter(InputElement.IsEnabledProperty, Boolean(value, property)),
        "margin" => new Setter(Layoutable.MarginProperty, Thickness(value, property)),
        "background" when IsTemplated(control) => new Setter(TemplatedControl.BackgroundProperty, Brush(value, property)),
        "foreground" when IsTemplated(control) => new Setter(TemplatedControl.ForegroundProperty, Brush(value, property)),
        "fontSize" when IsTemplated(control) => new Setter(TemplatedControl.FontSizeProperty, Number(value, property)),
        "fontWeight" when IsTemplated(control) => new Setter(TemplatedControl.FontWeightProperty, CommonProperties.ParseFontWeight(Text(value, property))),
        "fontStyle" when IsTemplated(control) => new Setter(TemplatedControl.FontStyleProperty, CommonProperties.ParseFontStyle(Text(value, property))),
        "padding" when IsTemplated(control) => new Setter(TemplatedControl.PaddingProperty, Thickness(value, property)),
        "horizontalAlignment" => new Setter(Layoutable.HorizontalAlignmentProperty, Horizontal(value, property)),
        "verticalAlignment" => new Setter(Layoutable.VerticalAlignmentProperty, Vertical(value, property)),
        _ => throw new ArgumentException($"Style property '{property}' is not supported for selector '{control}'."),
    };

    private static bool IsTemplated(string control) => control is
        "Window" or "Button" or "TextBox" or "PasswordBox" or "CheckBox" or "RadioButton" or
        "ToggleSwitch" or "ComboBox" or "ListBox" or "NumericUpDown" or "DatePicker" or
        "TimePicker" or "Slider" or "ProgressBar" or "TabControl" or "TabItem" or "Menu" or "MenuItem";

    private static object Primitive(JsonElement value, string description) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(item => item.GetDouble()).ToArray(),
        _ => throw new ArgumentException($"The {description} must be a string, number, boolean, or numeric thickness tuple."),
    };

    private static double Number(object value, string property) => value is double number && double.IsFinite(number)
        ? number
        : throw new ArgumentException($"Style property '{property}' requires a finite number.");
    private static bool Boolean(object value, string property) => value is bool boolean
        ? boolean
        : throw new ArgumentException($"Style property '{property}' requires a boolean.");
    private static string Text(object value, string property) => value as string
        ?? throw new ArgumentException($"Style property '{property}' requires a string.");
    private static IBrush Brush(object value, string property) =>
        Avalonia.Media.Brush.Parse(Text(value, property));
    private static Thickness Thickness(object value, string property) => value switch
    {
        double uniform when double.IsFinite(uniform) => new Thickness(uniform),
        double[] { Length: 2 } pair => new Thickness(pair[1], pair[0], pair[1], pair[0]),
        double[] { Length: 4 } edges => new Thickness(edges[3], edges[0], edges[1], edges[2]),
        _ => throw new ArgumentException($"Style property '{property}' requires a finite thickness."),
    };
    private static HorizontalAlignment Horizontal(object value, string property) => Text(value, property) switch
    {
        "left" => HorizontalAlignment.Left,
        "center" => HorizontalAlignment.Center,
        "right" => HorizontalAlignment.Right,
        "stretch" => HorizontalAlignment.Stretch,
        var item => throw new ArgumentException($"Unsupported horizontal alignment '{item}'."),
    };
    private static VerticalAlignment Vertical(object value, string property) => Text(value, property) switch
    {
        "top" => VerticalAlignment.Top,
        "center" => VerticalAlignment.Center,
        "bottom" => VerticalAlignment.Bottom,
        "stretch" => VerticalAlignment.Stretch,
        var item => throw new ArgumentException($"Unsupported vertical alignment '{item}'."),
    };

    private sealed record ContractModel(Dictionary<string, JsonElement>? Resources, StyleModel[]? Styles);
    private sealed record StyleModel(SelectorModel? Selector, Dictionary<string, JsonElement>? Setters);
    private sealed record SelectorModel(string Control, string[]? Classes);
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(ContractModel))]
    private sealed partial class StyleJsonContext : JsonSerializerContext;
}
