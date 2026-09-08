using Avalonia;
using Avalonia.Headless;

namespace SharpTS.Gui.Conformance.Tests;

// Avalonia's platform is process-wide. Every in-process fixture must use the same
// real renderer, regardless of which test class xUnit happens to run first.
internal static class DesktopTestPlatform
{
    internal static void EnsureInitialized()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<TestApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
                ShouldRenderOnUIThread = true,
            })
            .SetupWithoutStarting();
    }

    private sealed class TestApplication : Application { }
}
