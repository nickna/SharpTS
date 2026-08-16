using System.Runtime.CompilerServices;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Module initializer that wires up testhost-wide infrastructure once, before any test
/// or fixture runs. Today this just installs <see cref="AsyncLocalConsoleRedirector"/> as
/// <see cref="Console.Out"/> so compiled-mode tests can run in-process with parallel
/// output capture.
/// </summary>
internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        AsyncLocalConsoleRedirector.Install();
    }
}
