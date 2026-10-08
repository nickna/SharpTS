#pragma warning disable SHARPTS_HOSTING001

using System.Reflection;
using SharpTS.Hosting;
using SharpTS.Tests.Hosting;

namespace SharpTS.HostedInitializationFixture;

public static class HostedInitializationFixtureMarker { }

internal static class Program
{
    private static void Main(string[] args)
    {
        var dispatcher = new DeterministicHostDispatcher();
        var errors = new RecordingErrorSink();
        var lifetime = new RecordingLifetime();
        using (var runtime = SharpTSHostedAssembly.CreateRuntime(
            Assembly.LoadFile(Path.GetFullPath(args.Single())), dispatcher, lifetime, errors))
        {
            Task initialization = runtime.InitializeAsync();
            dispatcher.RunUntil(() => initialization.IsCompleted, timeout: TimeSpan.FromSeconds(30));
            initialization.GetAwaiter().GetResult();
            if (runtime.State != SharpTSHostedRuntimeState.Running)
                throw new InvalidOperationException($"Hosted initialization ended in state {runtime.State}.");
        }
        if (errors.Errors.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Errors));
        if (lifetime.Exits.Any(exit => exit.ExitCode != 0))
            throw new InvalidOperationException("Hosted guest requested an unsuccessful exit.");
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "SharpTS"))
            throw new InvalidOperationException("Hosted execution loaded SharpTS.dll.");
    }
}
