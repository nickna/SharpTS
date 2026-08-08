using System;
using System.IO;
using System.Reflection;

namespace SharpTS.Gui.Generated;

internal static class Launcher
{
    [STAThread]
    public static int Main(string[] args)
    {
        string developmentManifest = Path.Combine(AppContext.BaseDirectory, ".sharpts", "app.json");
        return File.Exists(developmentManifest)
            ? global::SharpTS.Gui.Host.Program.Main(args)
            : global::SharpTS.Gui.Host.Program.MainEmbedded(args, Assembly.GetExecutingAssembly());
    }
}
