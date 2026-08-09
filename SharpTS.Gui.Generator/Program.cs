using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 1 || args[0] is not ("generate" or "verify"))
{
    Console.Error.WriteLine("Usage: SharpTS.Gui.Generator <generate|verify>");
    return 2;
}

string root = FindRoot(AppContext.BaseDirectory);
string manifestPath = Path.Combine(root, "SharpTS.Gui", "Controls", "controls.v1.json");
JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
    ?? throw new InvalidOperationException("The GUI control manifest is empty.");
Validate(manifest);
string hash = Hash(CanonicalSemantic(manifest));

var outputs = new Dictionary<string, string>
{
    [Path.Combine(root, "SharpTS.Gui", "Generated", "ControlContract.Generated.cs")] = CanonicalText(GenerateCSharp(manifest, hash)),
    [Path.Combine(root, "SharpTS.Gui.Sdk", "GuiPackage", "control-surface.generated.ts")] = CanonicalText(GenerateTypeScript(manifest, hash)),
    [Path.Combine(root, "SharpTS.Gui.Sdk", "GuiPackage", "control-docs.generated.json")] = CanonicalText(GenerateDocumentation(manifest, hash)),
    [Path.Combine(root, "SharpTS.Gui.Sdk", "Sdk", "DescriptorContract.Generated.props")] = CanonicalText(GenerateSdkProps(hash)),
};

bool stale = false;
foreach ((string path, string content) in outputs)
{
    if (args[0] == "generate")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        Console.WriteLine(Path.GetRelativePath(root, path));
    }
    else if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"Generated GUI contract is stale: {Path.GetRelativePath(root, path)}");
        stale = true;
    }
}
return stale ? 1 : 0;

static string CanonicalText(string value) =>
    value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

static string FindRoot(string start)
{
    for (string? directory = Path.GetFullPath(start); directory is not null; directory = Path.GetDirectoryName(directory))
        if (File.Exists(Path.Combine(directory, "SharpTS.sln"))) return directory;
    throw new InvalidOperationException("Could not locate SharpTS.sln.");
}

static void Validate(JsonObject manifest)
{
    if (manifest["schemaVersion"]?.GetValue<int>() != 1)
        throw new InvalidOperationException("Only GUI control manifest schema version 1 is supported.");
    var adapters = manifest["reservedAdapterIds"]!.AsArray().Select(item => item!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
    var kinds = new HashSet<string>(StringComparer.Ordinal);
    foreach (JsonObject control in manifest["controls"]!.AsArray().Select(item => item!.AsObject()))
    {
        string kind = Required(control, "kind");
        if (!kinds.Add(kind)) throw new InvalidOperationException($"Duplicate control kind '{kind}'.");
        string adapter = Required(control, "adapter");
        if (!adapters.Contains(adapter)) throw new InvalidOperationException($"Control '{kind}' uses unknown adapter '{adapter}'.");
        var props = new HashSet<string>(StringComparer.Ordinal);
        foreach (string groupName in control["groups"]!.AsArray().Select(item => item!.GetValue<string>()))
        {
            if (manifest["propertyGroups"]![groupName] is not JsonArray group)
                throw new InvalidOperationException($"Control '{kind}' uses unknown property group '{groupName}'.");
            foreach (JsonObject prop in group.Select(item => item!.AsObject()))
                if (!props.Add(Required(prop, "name"))) throw new InvalidOperationException($"Duplicate prop '{Required(prop, "name")}' on '{kind}'.");
        }
        foreach (JsonObject prop in control["props"]!.AsArray().Select(item => item!.AsObject()))
            if (!props.Add(Required(prop, "name"))) throw new InvalidOperationException($"Duplicate prop '{Required(prop, "name")}' on '{kind}'.");
        foreach (JsonObject evt in control["events"]!.AsArray().Select(item => item!.AsObject()))
            if (!props.Add(Required(evt, "name"))) throw new InvalidOperationException($"Duplicate prop/event '{Required(evt, "name")}' on '{kind}'.");
    }
}

static string Required(JsonObject value, string name) =>
    value[name]?.GetValue<string>() is { Length: > 0 } result ? result : throw new InvalidOperationException($"Missing required '{name}'.");

static string CanonicalSemantic(JsonObject manifest)
{
    JsonNode clone = manifest.DeepClone();
    RemoveDocumentation(clone);
    return Canonical(clone);
}

static void RemoveDocumentation(JsonNode? node)
{
    if (node is JsonObject obj)
    {
        obj.Remove("documentation");
        foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray()) RemoveDocumentation(child);
    }
    else if (node is JsonArray array)
        foreach (JsonNode? child in array) RemoveDocumentation(child);
}

static string Canonical(JsonNode? node) => node switch
{
    null => "null",
    JsonObject obj => "{" + string.Join(",", obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => JsonSerializer.Serialize(pair.Key) + ":" + Canonical(pair.Value))) + "}",
    JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
    _ => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
};

static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

static string GenerateCSharp(JsonObject manifest, string hash)
{
    var text = new StringBuilder("// <auto-generated />\nnamespace SharpTS.Gui;\n\n");
    text.AppendLine("internal static class GeneratedControlContract");
    text.AppendLine("{");
    text.AppendLine("    internal const int SchemaVersion = 1;");
    text.AppendLine($"    internal const string SchemaHash = \"{hash}\";");
    text.AppendLine("    internal static NodeDescriptor[] CreateDescriptors() =>");
    text.AppendLine("    [");
    foreach (JsonObject control in manifest["controls"]!.AsArray().Select(item => item!.AsObject()))
        text.AppendLine($"        {DescriptorExpression(Required(control, "adapter"))},");
    text.AppendLine("    ];");
    text.AppendLine("}");
    return text.ToString().Replace("\r\n", "\n");
}

static string DescriptorExpression(string adapter) => adapter switch
{
    "window" => "new WindowDescriptor()",
    "stack-panel" => "new StackPanelDescriptor(\"StackPanel\")",
    "toolbar" => "new StackPanelDescriptor(\"ToolBar\")",
    "wrap-panel" => "new WrapPanelDescriptor()",
    "dock-panel" => "new DockPanelDescriptor()",
    "grid" => "new GridDescriptor()",
    "border" => "new BorderDescriptor()",
    "statusbar" => "new BorderDescriptor(\"StatusBar\")",
    "scroll-viewer" => "new ScrollViewerDescriptor()",
    "text-block" => "new TextBlockDescriptor()",
    "button" => "new ButtonDescriptor()",
    "text-box" => "new TextBoxDescriptor()",
    "password-box" => "new TextBoxDescriptor(\"PasswordBox\")",
    "check-box" => "new CheckBoxDescriptor()",
    "radio-button" => "new CheckBoxDescriptor(\"RadioButton\")",
    "toggle-switch" => "new CheckBoxDescriptor(\"ToggleSwitch\")",
    "combo-box" => "new ComboBoxDescriptor()",
    "slider" => "new SliderDescriptor()",
    "progress-bar" => "new ProgressBarDescriptor()",
    "separator" => "new SeparatorDescriptor()",
    "list-box" => "new ListBoxDescriptor()",
    "numeric-up-down" => "new NumericUpDownDescriptor()",
    "date-picker" => "new DatePickerDescriptor()",
    "time-picker" => "new TimePickerDescriptor()",
    "image" => "new ImageDescriptor()",
    "tab-control" => "new TabControlDescriptor()",
    "tab-item" => "new TabItemDescriptor()",
    "menu" => "new MenuDescriptor()",
    "menu-item" => "new MenuItemDescriptor()",
    "items-control" => "new ItemsHostDescriptor(\"ItemsControl\")",
    "virtualizing-list" => "new VirtualizingListDescriptor()",
    "tree-view" => "new ItemsHostDescriptor(\"TreeView\")",
    "tree-view-item" => "new TreeViewItemDescriptor()",
    "canvas" => "new CanvasDescriptor()",
    "rich-text-block" => "new RichTextBlockDescriptor()",
    "drawing-canvas" => "new DrawingCanvasDescriptor()",
    _ => throw new InvalidOperationException($"No descriptor adapter implementation for '{adapter}'."),
};

static string GenerateTypeScript(JsonObject manifest, string hash)
{
    var text = new StringBuilder("// <auto-generated />\n");
    text.AppendLine("import type { CommonProps, GuiChild, GuiElement, TextualChild, ControlRef, WindowHandle, StackPanelHandle, GridHandle, BorderHandle, TextBlockHandle, ButtonHandle, TextBoxHandle, Thickness, HorizontalAlignment, VerticalAlignment, Orientation, ScrollBarVisibility, Theme, Stretch, SelectionMode, Dock, FontWeight, TextAlignment, ContentStyleProps, TextStyleProps, RichTextRun, DrawingCommand } from \"./runtime-types\";");
    text.AppendLine($"export const descriptorSchemaVersion = 1;\nexport const descriptorSchemaHash = \"{hash}\";");
    text.AppendLine("export interface ChildrenProps { children?: GuiChild; }");
    text.AppendLine("export interface TextualChildrenProps { children?: TextualChild; }");
    text.AppendLine("export interface SingleElementChildProps { children?: GuiElement | null | undefined; }");
    foreach (JsonObject control in manifest["controls"]!.AsArray().Select(item => item!.AsObject()))
    {
        string kind = Required(control, "kind");
        string propsType = Required(control, "propsType");
        string handle = Required(control, "handle");
        string childBase = control["children"]!["model"]!.GetValue<string>() switch { "many" => ", ChildrenProps", "text" => ", TextualChildrenProps", "singleElement" => ", SingleElementChildProps", _ => "" };
        var extraBases = new List<string>();
        var groups = control["groups"]!.AsArray().Select(item => item!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        if (groups.Contains("contentStyle")) extraBases.Add("ContentStyleProps");
        else if (groups.Contains("textStyle")) extraBases.Add("TextStyleProps");
        string bases = $" extends CommonProps<{handle}>{childBase}" + (extraBases.Count == 0 ? "" : ", " + string.Join(", ", extraBases));
        text.Append($"export interface {propsType}{bases} {{");
        foreach (JsonObject prop in control["props"]!.AsArray().Select(item => item!.AsObject()))
            text.Append($" {Required(prop, "name")}{(prop["required"]?.GetValue<bool>() == true ? "" : "?")}: {Required(prop, "type")};");
        foreach (JsonObject evt in control["events"]!.AsArray().Select(item => item!.AsObject()))
            text.Append($" {Required(evt, "name")}?: {Required(evt, "type")};");
        text.AppendLine(" }");
    }
    text.AppendLine("export type SeparatorProps = CommonProps<unknown>;");
    text.AppendLine("type DesktopTag<TProps> = (props: TProps) => GuiElement;");
    text.AppendLine("function tag<TProps>(name: string): DesktopTag<TProps> { return name as any; }");
    foreach (JsonObject control in manifest["controls"]!.AsArray().Select(item => item!.AsObject()))
    {
        string kind = Required(control, "kind");
        text.AppendLine($"export const {kind} = tag<{Required(control, "propsType")}>({JsonSerializer.Serialize(kind)});");
    }
    text.AppendLine("export const controlContract = [");
    foreach (JsonObject control in manifest["controls"]!.AsArray().Select(item => item!.AsObject()))
    {
        var names = new List<string>();
        foreach (string group in control["groups"]!.AsArray().Select(item => item!.GetValue<string>()))
            names.AddRange(manifest["propertyGroups"]![group]!.AsArray().Select(p => Required(p!.AsObject(), "name")));
        names.AddRange(control["props"]!.AsArray().Select(p => Required(p!.AsObject(), "name")));
        names.AddRange(control["events"]!.AsArray().Select(p => Required(p!.AsObject(), "name")));
        text.AppendLine($"  {{ kind: {JsonSerializer.Serialize(Required(control, "kind"))}, props: [{string.Join(", ", names.Select(name => JsonSerializer.Serialize(name)))}] }},");
    }
    text.AppendLine("] as const;");
    return text.ToString().Replace("\r\n", "\n");
}

static string GenerateDocumentation(JsonObject manifest, string hash)
{
    var controls = new JsonArray();
    foreach (JsonObject source in manifest["controls"]!.AsArray().Select(item => item!.AsObject()))
    {
        var props = new JsonArray();
        foreach (string group in source["groups"]!.AsArray().Select(item => item!.GetValue<string>()))
            foreach (JsonObject prop in manifest["propertyGroups"]![group]!.AsArray().Select(item => item!.AsObject())) props.Add(DocProp(prop));
        foreach (JsonObject prop in source["props"]!.AsArray().Select(item => item!.AsObject())) props.Add(DocProp(prop));
        foreach (JsonObject evt in source["events"]!.AsArray().Select(item => item!.AsObject())) props.Add(DocProp(evt));
        controls.Add(new JsonObject { ["kind"] = Required(source, "kind"), ["documentation"] = source["documentation"]?.GetValue<string>() ?? "", ["props"] = props });
    }
    var output = new JsonObject { ["schemaVersion"] = 1, ["schemaHash"] = hash, ["controls"] = controls };
    return output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
}

static string GenerateSdkProps(string hash) =>
    $"<Project>\n  <!-- <auto-generated /> -->\n  <PropertyGroup>\n    <SharpTSGuiDescriptorSchemaVersion>1</SharpTSGuiDescriptorSchemaVersion>\n    <SharpTSGuiDescriptorSchemaHash>{hash}</SharpTSGuiDescriptorSchemaHash>\n  </PropertyGroup>\n</Project>\n";

static JsonObject DocProp(JsonObject prop) => new()
{
    ["name"] = Required(prop, "name"),
    ["type"] = Required(prop, "type"),
    ["documentation"] = prop["documentation"]?.GetValue<string>() ?? "",
    ["enumValues"] = prop["enumValues"]?.DeepClone(),
};
