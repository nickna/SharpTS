using SharpTS.Gui.ConformanceSupport;

namespace SharpTS.Gui.ConformanceHost;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        _ = typeof(DesktopConformanceSupportBridge).Assembly;
        return SharpTS.Gui.Host.Program.Main(args);
    }
}
