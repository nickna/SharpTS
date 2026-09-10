using Avalonia;
using Avalonia.Markup.Xaml;

namespace SharpTS.Gui.Host;

public sealed partial class GuiApplication : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
