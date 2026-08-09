using System;
using System.IO;
using System.Reflection;

namespace SharpTS.Gui.Generated;

internal static class Launcher
{
    [STAThread]
    public static int Main(string[] args)
    {
        using IDisposable controlProviders = ControlProviderRegistration.Register();
        string developmentManifest = Path.Combine(AppContext.BaseDirectory, ".sharpts", "app.json");
#if SHARPTS_GUI_COMPILED_ONLY
        if (File.Exists(developmentManifest) &&
            Array.IndexOf(args, "--mode") < 0)
        {
            var compiledArgs = new string[args.Length + 2];
            compiledArgs[0] = "--mode";
            compiledArgs[1] = "compiled";
            Array.Copy(args, 0, compiledArgs, 2, args.Length);
            args = compiledArgs;
        }
#endif
        return File.Exists(developmentManifest)
            ? global::SharpTS.Gui.Host.Program.Main(args)
            : global::SharpTS.Gui.Host.Program.MainEmbedded(args, Assembly.GetExecutingAssembly());
    }
}
