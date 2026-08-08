using Avalonia;
using Avalonia.Themes.Fluent;

namespace SharpTS.Gui.Host;

public sealed class GuiApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}
