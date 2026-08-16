using System.Runtime.CompilerServices;
using SharpTS.Runtime;

namespace SharpTS.Tests.Infrastructure;

internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SloppyModeWarnings.Enabled = false;
    }
}
