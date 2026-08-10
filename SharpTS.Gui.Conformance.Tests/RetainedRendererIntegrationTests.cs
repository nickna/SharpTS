using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class RetainedRendererIntegrationTests
{
    [Fact]
    public async Task HeadlessScenario_ReconcilesIdenticallyInInterpretedAndCompiledModes()
    {
        TraceEvent[] interpreted = await RunHostAsync("interpreted");
        TraceEvent[] compiled = await RunHostAsync("compiled");

        ValidateMode(interpreted, "interpreted");
        ValidateMode(compiled, "compiled");

        static bool IsRendererEvent(TraceEvent item) =>
            item.Stage.StartsWith("reconcile-", StringComparison.Ordinal) ||
            item.Stage.StartsWith("view-render-", StringComparison.Ordinal) ||
            item.Stage is "mount" or "render-commit" or
                "subscribe" or "unsubscribe" or "ref-attach" or "ref-detach" or
                "coalesced-update-complete" or "dependency-switch-complete" or
                "reactive-update-complete" or "transient-ref-cleaned";

        Assert.Equal(
            interpreted.Where(IsRendererEvent).Select(item => (item.Stage, item.Detail)).ToArray(),
            compiled.Where(IsRendererEvent).Select(item => (item.Stage, item.Detail)).ToArray());
    }

    private static void ValidateMode(TraceEvent[] events, string mode)
    {
        TraceEvent setup = events.Single(item => item.Stage == "avalonia-setup");
        int owner = setup.Thread;
        string? ownerContext = setup.Context;
        Assert.False(string.IsNullOrWhiteSpace(ownerContext));

        Assert.All(events.Where(item => item.Stage != "task-complete-off-thread"), item =>
        {
            Assert.Equal(owner, item.Thread);
            Assert.Equal(ownerContext, item.Context);
        });
        Assert.NotEqual(owner, events.Single(item => item.Stage == "task-complete-off-thread").Thread);

        Assert.Equal(
            new[]
            {
                "view-render-1", "view-render-2", "view-render-3", "view-render-4",
                "view-render-5", "view-render-6", "view-render-7", "view-render-8"
            },
            events.Where(item => item.Stage.StartsWith("view-render-", StringComparison.Ordinal))
                .Select(item => item.Stage));
        AssertStageOrder(events, "view-render-2", "coalesced-update-complete");
        AssertStageOrder(events, "view-render-3", "dependency-switch-complete");
        AssertStageOrder(events, "view-render-4", "reactive-update-complete");
        AssertStageOrder(events, "guest-init-end", "dispatcher-sentinel");
        AssertStageOrder(events, "dispatcher-sentinel", "guest-timer");

        Assert.Single(events, item => item.Stage == "button-click-event");
        Assert.Single(events, item => item.Stage == "guest-click");
        Assert.Single(events, item => item.Stage == "form-text:User");
        Assert.Single(events, item => item.Stage == "form-check:false");
        Assert.Single(events, item => item.Stage == "form-choice:2");
        Assert.Single(events, item => item.Stage == "form-slider:9");
        AssertStageOrder(events, "forms-events-complete", "reactive-update-complete");
        Assert.DoesNotContain(events, item => item.Stage == "stale-guest-click");
        Assert.DoesNotContain(events, item => item.Stage == "pump");
        Assert.Contains(events, item => item.Stage == "transient-ref-cleaned");
        Assert.Contains(events, item => item.Stage == "late-reactive-work-ignored");
        AssertStageOrder(events, "late-reactive-work-ignored", "unmount");
        Assert.Single(events, item => item.Stage == "effect-setup");
        Assert.Contains(events, item => item.Stage == "effect-state-applied");
        Assert.Single(events, item => item.Stage == "effect-cleanup");
        AssertStageOrder(events, "mount", "effect-setup");
        AssertStageOrder(events, "effect-cleanup", "unmount");
        Assert.Contains(events, item => item.Stage == "render-boundary-fallback");
        Assert.Single(events, item => item.Stage == "effect-failure-setup");
        Assert.Contains(events, item => item.Stage == "effect-boundary-fallback");
        Assert.Equal(2, events.Count(item => item.Stage == "native-commit-boundary-fallback"));
        Assert.Contains(events, item => item.Stage == "native-commit-repeated-failure-complete");
        Assert.Contains(events, item => item.Stage == "native-commit-reset-success");
        Assert.True(
            events.Single(item => item.Stage == "effect-failure-setup").Sequence <
            events.First(item => item.Stage == "effect-boundary-fallback").Sequence);

        TraceEvent[] subscriptions = events.Where(item => item.Stage == "subscribe").ToArray();
        TraceEvent[] unsubscriptions = events.Where(item => item.Stage == "unsubscribe").ToArray();
        Assert.Equal(
            new[]
            {
                "TextBox#name", "CheckBox#enabled", "ComboBox#choice", "Slider#amount",
                "Button#action", "Button#transient", "Button#replacement"
            },
            subscriptions.Select(item => item.Detail));
        Assert.Equal(
            new[]
            {
                "Button#transient", "TextBox#name", "CheckBox#enabled", "ComboBox#choice",
                "Slider#amount", "Button#action", "Button#replacement"
            },
            unsubscriptions.Select(item => item.Detail));

        int activeSubscriptions = 0;
        int maximumSubscriptions = 0;
        foreach (TraceEvent item in events)
        {
            if (item.Stage == "subscribe")
                activeSubscriptions++;
            else if (item.Stage == "unsubscribe")
                activeSubscriptions--;
            maximumSubscriptions = Math.Max(maximumSubscriptions, activeSubscriptions);
            Assert.True(activeSubscriptions >= 0, $"{mode} produced a negative subscription count.");
        }
        Assert.Equal(0, activeSubscriptions);
        Assert.Equal(6, maximumSubscriptions);

        KeyedIdentity[] initial = ParseIdentities(
            events.Single(item => item.Stage == "identities-initial").Detail!);
        KeyedIdentity[] reordered = ParseIdentities(
            events.Single(item => item.Stage == "identities-reordered").Detail!);
        KeyedIdentity[] final = ParseIdentities(
            events.Single(item => item.Stage == "identities-final").Detail!);
        Assert.True(IndexOf(initial, "a") < IndexOf(initial, "b"));
        Assert.True(IndexOf(reordered, "b") < IndexOf(reordered, "a"));
        Assert.DoesNotContain(reordered, item => item.Key == "transient");

        foreach (string key in new[]
        {
            "window", "shell", "scroll", "panel", "status", "form", "name", "enabled",
            "choice", "amount", "progress", "action", "a", "b"
        })
        {
            KeyedIdentity before = initial.Single(item => item.Key == key);
            KeyedIdentity after = reordered.Single(item => item.Key == key);
            KeyedIdentity last = final.Single(item => item.Key == key);
            Assert.Equal(before.Id, after.Id);
            Assert.Equal(after.Id, last.Id);
            Assert.Equal(before.Kind, after.Kind);
        }

        foreach (string key in new[] { "$component-a/0", "$component-b/0" })
        {
            Assert.Equal(initial.Single(item => item.Key == key).Id, reordered.Single(item => item.Key == key).Id);
            Assert.Equal(reordered.Single(item => item.Key == key).Id, final.Single(item => item.Key == key).Id);
        }
        Assert.DoesNotContain(initial, item => item.Key == "transparent-pair");
        Assert.Contains(initial, item => item.Key == "$transparent-pair/$fragment-a");
        Assert.Contains(initial, item => item.Key == "$transparent-pair/$fragment-b");

        KeyedIdentity oldReplacement = initial.Single(item => item.Key == "replacement");
        KeyedIdentity newReplacement = reordered.Single(item => item.Key == "replacement");
        Assert.Equal("TextBlock", oldReplacement.Kind);
        Assert.Equal("Button", newReplacement.Kind);
        Assert.NotEqual(oldReplacement.Id, newReplacement.Id);
        Assert.Equal(newReplacement.Id, final.Single(item => item.Key == "replacement").Id);

        long lastCleanup = events
            .Where(item => item.Stage is "unsubscribe" or "ref-detach")
            .Max(item => item.Sequence);
        Assert.True(lastCleanup < events.Single(item => item.Stage == "unmount").Sequence);
    }

    private static async Task<TraceEvent[]> RunHostAsync(string mode)
    {
        string repositoryRoot = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string hostDirectory = Path.Combine(
            repositoryRoot,
            "SharpTS.Gui.ConformanceHost",
            "bin",
            configuration,
            "net10.0");
        string hostAssembly = Path.Combine(hostDirectory, "SharpTS.Gui.ConformanceHost.dll");
        string tracePath = Path.Combine(Path.GetTempPath(), $"sharpts-gui-renderer-{mode}-{Guid.NewGuid():N}.json");
        Assert.True(File.Exists(hostAssembly), $"GUI host was not built: {hostAssembly}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = hostDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(hostAssembly);
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add(mode);
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--trace");
        startInfo.ArgumentList.Add(tracePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the GUI host.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"GUI {mode} headless host exceeded 30 seconds.");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        Assert.True(process.ExitCode == 0,
            $"GUI {mode} host failed with {process.ExitCode}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
        Assert.True(File.Exists(tracePath), $"GUI {mode} trace was not produced.");

        using var trace = JsonDocument.Parse(await File.ReadAllTextAsync(tracePath));
        return trace.RootElement.EnumerateArray().Select(item => new TraceEvent(
            item.GetProperty("Sequence").GetInt64(),
            item.GetProperty("Stage").GetString()!,
            item.GetProperty("ManagedThreadId").GetInt32(),
            item.GetProperty("SynchronizationContext").ValueKind == JsonValueKind.Null
                ? null
                : item.GetProperty("SynchronizationContext").GetString(),
            item.GetProperty("Detail").ValueKind == JsonValueKind.Null
                ? null
                : item.GetProperty("Detail").GetString())).ToArray();
    }

    private static KeyedIdentity[] ParseIdentities(string detail) =>
        detail.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select((entry, index) =>
            {
                int equals = entry.IndexOf('=');
                int colon = entry.LastIndexOf(':');
                return new KeyedIdentity(
                    entry[..equals],
                    int.Parse(entry[(equals + 1)..colon], System.Globalization.CultureInfo.InvariantCulture),
                    entry[(colon + 1)..],
                    index);
            })
            .ToArray();

    private static int IndexOf(KeyedIdentity[] identities, string key) =>
        identities.Single(item => item.Key == key).Order;

    private static void AssertStageOrder(TraceEvent[] events, string before, string after) =>
        Assert.True(
            events.Single(item => item.Stage == before).Sequence <
            events.Single(item => item.Stage == after).Sequence,
            $"Expected '{before}' before '{after}'.");

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "SharpTS.sln")))
                return directory;
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("Could not locate the SharpTS repository root.");
    }

    private sealed record TraceEvent(
        long Sequence,
        string Stage,
        int Thread,
        string? Context,
        string? Detail);

    private sealed record KeyedIdentity(string Key, int Id, string Kind, int Order);
}
